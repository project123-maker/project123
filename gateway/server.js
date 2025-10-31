// gateway/server.js
// npm i express cors body-parser firebase-admin fs-extra dotenv
const express = require('express');
const cors = require('cors');
const body = require('body-parser');
const fs = require('fs-extra');
const path = require('path');
const admin = require('firebase-admin');
require('dotenv').config();

const PORT = process.env.PORT || 7070;
const LOCK_TTL_SEC = 120;

const app = express();
app.use(cors());
app.use(body.json({ limit: '256kb' }));

// ----- Firebase Admin -----
const hasEnvKey = !!process.env.FIREBASE_PRIVATE_KEY;
if (!admin.apps.length) {
    admin.initializeApp({
        credential: hasEnvKey
            ? admin.credential.cert({
                projectId: process.env.FIREBASE_PROJECT_ID,
                clientEmail: process.env.FIREBASE_CLIENT_EMAIL,
                privateKey: process.env.FIREBASE_PRIVATE_KEY.replace(/\\n/g, '\n'),
            })
            : admin.credential.cert(path.join(__dirname, 'serviceAccount.json')),
    });
}
const db = admin.firestore();

// ----- Paths -----
const ROOT = path.resolve(__dirname, '..');
const SB_DIR = path.join(ROOT, 'bin', 'sing-box');
const SB_CFG = path.join(SB_DIR, 'config.json');
const LAST_VLESS = path.join(SB_DIR, 'last.vless.txt');
const DEVICE_FILE = path.join(SB_DIR, 'device.id');

fs.ensureDirSync(SB_DIR);

// ----- Helpers -----
const now = admin.firestore.Timestamp.now;

async function readDeviceId() {
    try {
        return (await fs.readFile(DEVICE_FILE, 'utf8')).trim();
    } catch {
        const id = [...cryptoRandom(16)].map(b => b.toString(16).padStart(2, '0')).join('');
        await fs.writeFile(DEVICE_FILE, id);
        return id;
    }
}
function cryptoRandom(n) {
    const { randomBytes } = require('crypto');
    return randomBytes(n);
}

function codeRef(code) { return db.collection('codes').doc(code); }
function configRef(pathStr) {
    // path like "configs/current" or "configs/<region>/<doc>"
    const parts = pathStr.split('/').filter(Boolean);
    let col = db.collection(parts.shift());
    while (parts.length > 1) { const d = parts.shift(); col = col.doc(d).collection(parts.shift()); }
    return col.doc(parts.shift());
}

function isStaleLock(lockTs) {
    if (!lockTs) return true;
    const ageSec = (Date.now() - lockTs.toDate().getTime()) / 1000;
    return ageSec > LOCK_TTL_SEC;
}

async function getVlessForCodeOrDefault(redeemCode) {
    // If code exists -> use vlessPath; else fallback to configs/current
    if (redeemCode) {
        const snap = await codeRef(redeemCode).get();
        if (snap.exists) {
            const pathStr = snap.get('vlessPath') || 'configs/current';
            const cfg = await configRef(pathStr).get();
            const vless = cfg.get('vless') || '';
            if (!vless.startsWith('vless://')) throw new Error('Invalid VLESS');
            return { vless, pathStr };
        }
    }
    // fallback to local file (offline) OR Firestore configs/current
    try {
        const local = JSON.parse(await fs.readFile(path.join(__dirname, 'local.json'), 'utf8'));
        if (typeof local.vless === 'string' && local.vless.startsWith('vless://')) {
            return { vless: local.vless, pathStr: 'local' };
        }
    } catch { }
    const cfg = await configRef('configs/current').get();
    const vless = cfg.get('vless') || '';
    if (!vless.startsWith('vless://')) throw new Error('Invalid VLESS');
    return { vless, pathStr: 'configs/current' };
}

function singboxConfigFromVLESS(vlessUrl) {
    // Minimal, robust VLESS parser + sane defaults (DNS over HTTPS + MTU 1400)
    const u = new URL(vlessUrl);
    if (u.protocol !== 'vless:') throw new Error('Bad scheme');

    const id = u.username; // UUID
    const host = u.hostname;
    const port = Number(u.port || 443);
    const params = Object.fromEntries(u.searchParams.entries());
    const sni = params.sni || params.host || host;
    const fp = params.fp || 'chrome';
    const alpn = (params.alpn || 'h2,http/1.1').split(',');

    const serverName = u.hash ? decodeURIComponent(u.hash.substring(1)) : 'svpn';

    return {
        log: { level: 'info', timestamp: true },
        experimental: { clash_api: { external_controller: '127.0.0.1:0' } },
        dns: {
            servers: [
                { tag: 'doh', address: 'https://cloudflare-dns.com/dns-query', detour: 'direct' },
                { tag: 'block', address: 'rcode://success' }
            ],
            rules: [
                { outbound: 'any', server: 'doh' }
            ],
            strategy: 'ipv4_only'
        },
        inbounds: [
            { type: 'tun', tag: 'tun-in', mtu: 1400, strict_route: true, auto_route: true, sniff: true }
        ],
        outbounds: [
            {
                tag: 'vless-out',
                type: 'vless',
                server: host,
                server_port: port,
                uuid: id,
                flow: '',
                packet_encoding: 'xudp',
                tls: {
                    enabled: true,
                    server_name: sni,
                    insecure: false,
                    utls: { enabled: true, fingerprint: fp },
                    alpn
                },
                transport: { type: 'ws', path: u.pathname || '/', headers: { Host: sni } }
            },
            { tag: 'direct', type: 'direct' },
            { tag: 'block', type: 'block' }
        ],
        route: {
            geoip: { path: 'geoip.db' },
            geosite: { path: 'geosite.db' },
            rules: [
                { dns: true, outbound: 'vless-out' },
                { ip_is_private: true, outbound: 'direct' }
            ],
            final: 'vless-out',
            auto_detect_interface: true
        }
    };
}

async function writeConfigJson(vless) {
    const cfg = singboxConfigFromVLESS(vless);
    await fs.writeJson(SB_CFG, cfg, { spaces: 2 });
    await fs.writeFile(LAST_VLESS, vless);
    return true;
}

async function checkAndTakeLock(code, deviceId, platform) {
    if (!code) return { allowed: true }; // "default/current" mode: no lock
    const ref = codeRef(code);
    return db.runTransaction(async tr => {
        const snap = await tr.get(ref);
        if (!snap.exists) throw new Error('Code not found');
        const lock = snap.get('lock') || {};
        const lockedBy = lock.lockedBy || null;
        const when = lock.lockAt || null;

        const stale = isStaleLock(when);
        const same = lockedBy === deviceId;

        if (same || stale) {
            tr.update(ref, {
                'lock.lockedBy': deviceId,
                'lock.platform': platform,
                'lock.lockAt': now(),
            });
            return { allowed: true };
        }
        return { allowed: false };
    });
}

async function bumpHeartbeat(code, deviceId, platform) {
    if (!code) return;
    const ref = codeRef(code);
    await ref.update({
        'lock.lockedBy': deviceId,
        'lock.platform': platform,
        'lock.lockAt': now(),
    });
}

// ---------- Routes ----------

app.post('/redeem', async (req, res) => {
    const { redeemCode, deviceId, app: platform } = req.body || {};
    try {
        const { vless } = await getVlessForCodeOrDefault(redeemCode);
        // Don't expose VLESS or path
        res.json({ ok: true, redeemId: redeemCode || 'default', message: 'ok' });
    } catch (e) {
        res.json({ ok: false, message: e.message || 'Redeem failed' });
    }
});

app.post('/refresh', async (req, res) => {
    const { redeemId } = req.body || {};
    try {
        const { vless } = await getVlessForCodeOrDefault(redeemId);
        await writeConfigJson(vless);
        res.json({ ok: true, message: 'refreshed' });
    } catch (e) {
        res.json({ ok: false, message: e.message || 'Refresh failed' });
    }
});

app.post('/connect', async (req, res) => {
    const { redeemId, deviceId, app: platform } = req.body || {};
    try {
        const lock = await checkAndTakeLock(redeemId, deviceId, platform || 'desktop');
        if (!lock.allowed) return res.json({ ok: false, message: 'Single-device lock' });

        const { vless } = await getVlessForCodeOrDefault(redeemId);
        await writeConfigJson(vless);
        res.json({ ok: true, message: 'config ready' });
    } catch (e) {
        res.json({ ok: false, message: e.message || 'Connect failed' });
    }
});

app.post('/disconnect', async (_req, res) => {
    try {
        // Leave config.json as-is; client will kill sing-box.
        res.json({ ok: true });
    } catch {
        res.json({ ok: false });
    }
});

app.post('/heartbeat', async (req, res) => {
    const { redeemId, deviceId, app: platform } = req.body || {};
    try {
        const lock = await checkAndTakeLock(redeemId, deviceId, platform || 'desktop');
        if (lock.allowed) await bumpHeartbeat(redeemId, deviceId, platform || 'desktop');
        res.json({ ok: true, allowed: !!lock.allowed });
    } catch (e) {
        // If code missing, allow (works with "default" mode)
        res.json({ ok: true, allowed: !redeemId });
    }
});

app.listen(PORT, () => console.log(`Simple gateway on http://127.0.0.1:${PORT}`));
