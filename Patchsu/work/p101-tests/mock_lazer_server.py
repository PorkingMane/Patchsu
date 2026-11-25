"""Tiny mock of a lazer-compatible osu! server: OAuth, /me, chat (REST + notifications websocket), leaderboards.
Used to drive the patched client end to end. Logs every request so the test can check what the client sent."""
import asyncio, json, sys, time
from aiohttp import web, WSMsgType

PORT = int(sys.argv[1]); SCORES = sys.argv[2]
ME = {"id": 1001, "username": "patchsu-tester", "country_code": "JP", "country": {"code": "JP", "name": "Japan"},
      "avatar_url": "https://a.mamesosu.net/1001", "is_supporter": False, "is_active": True, "is_bot": False}
BOT = {"id": 2, "username": "Mame-Bot", "country_code": "JP", "avatar_url": "https://a.mamesosu.net/2"}
CH = {"channel_id": 1, "name": "#osu", "description": "general", "type": "PUBLIC", "last_message_id": 1, "moderated": False}
log, sockets, msgs = [], set(), [{"message_id": 1, "channel_id": 1, "sender_id": 2, "sender": BOT, "content": "welcome", "is_action": False, "timestamp": "2026-11-25T00:00:00+00:00"}]

def now(): return time.strftime("%Y-%m-%dT%H:%M:%S+00:00", time.gmtime())

@web.middleware
async def logger(req, handler):
    form = dict(await req.post()) if req.method in ("POST", "PUT") and req.content_type.startswith(("application/x-www", "multipart")) else {}
    log.append({"method": req.method, "path": req.path, "query": dict(req.query), "auth": req.headers.get("Authorization", "")[:15],
                "form": {k: v for k, v in form.items() if k != "password"}})
    req["form"] = form
    return await handler(req)

def authed(req):
    if req.headers.get("Authorization") != "Bearer mock-access":
        raise web.HTTPUnauthorized(text=json.dumps({"detail": "invalid_token"}), content_type="application/json")

async def token(req):
    f = req["form"]
    if f.get("client_id") != "5" or f.get("client_secret") != "FGc9GAtyHzeQDshWP5Ah7dega8hJACAJpQtw6OXk":
        return web.json_response({"error": "invalid_client"}, status=401)
    if f.get("grant_type") == "password" and f.get("username") != "patchsu-tester":
        return web.json_response({"error": "invalid_grant", "hint": "Invalid username or password."}, status=401)
    return web.json_response({"token_type": "Bearer", "expires_in": 86400, "access_token": "mock-access", "refresh_token": "mock-refresh"})

async def me(req): authed(req); return web.json_response(ME)
async def notifications(req):
    authed(req); return web.json_response({"has_more": False, "notifications": [], "notification_endpoint": f"ws://127.0.0.1:{PORT}/ws"})
async def channels(req): authed(req); return web.json_response([CH])
async def updates(req): authed(req); return web.json_response({"messages": [], "presence": [CH], "silences": []})
async def ack(req): authed(req); return web.json_response({"silences": []})
async def join(req): authed(req); return web.json_response(CH)
async def get_channel(req): authed(req); return web.json_response({"channel": CH, "users": [ME, BOT]})
async def get_messages(req): authed(req); return web.json_response(msgs)
async def mark_read(req): authed(req); return web.json_response({})
async def post_message(req):
    authed(req); f = req["form"]
    m = {"message_id": len(msgs) + 1, "channel_id": 1, "sender_id": ME["id"], "sender": ME, "content": f.get("message"), "is_action": f.get("is_action") == "true", "timestamp": now(), "uuid": f.get("uuid")}
    msgs.append(m)
    reply = {"message_id": len(msgs) + 1, "channel_id": 1, "sender_id": 2, "sender": BOT, "content": "echo: " + f.get("message", ""), "is_action": False, "timestamp": now()}
    msgs.append(reply)
    async def push():
        # Like osu-web: the sender gets their own message back over the websocket (this confirms the local echo by uuid),
        # then the bot's reply.
        for payload in ({"messages": [m], "users": [ME]}, {"messages": [reply], "users": [BOT]}):
            await asyncio.sleep(0.3)
            for ws in list(sockets):
                await ws.send_str(json.dumps({"event": "chat.message.new", "data": payload}))
    asyncio.ensure_future(push())
    return web.json_response(m)
async def scores(req): authed(req); return web.json_response(json.load(open(SCORES)))
async def ws(req):
    log.append({"method": "WS", "path": req.path, "auth": req.headers.get("Authorization", "")[:15]})
    sock = web.WebSocketResponse(); await sock.prepare(req); sockets.add(sock)
    async for m in sock:
        if m.type == WSMsgType.TEXT: log.append({"method": "WS-MSG", "path": req.path, "data": json.loads(m.data)})
    sockets.discard(sock); return sock
async def get_log(req): return web.json_response(log)
async def fallback(req): return web.json_response({"error": "not mocked"}, status=404)

app = web.Application(middlewares=[logger])
app.add_routes([web.post("/oauth/token", token), web.get("/api/v2/me/{tail:.*}", me), web.get("/api/v2/notifications", notifications),
    web.get("/api/v2/chat/channels", channels), web.get("/api/v2/chat/updates", updates), web.post("/api/v2/chat/ack", ack),
    web.put("/api/v2/chat/channels/{c}/users/{u}", join), web.get("/api/v2/chat/channels/{c}", get_channel), web.get("/api/v2/chat/channels/{c}/messages", get_messages),
    web.post("/api/v2/chat/channels/{c}/messages", post_message), web.put("/api/v2/chat/channels/{c}/mark-as-read/{m}", mark_read),
    web.get("/api/v2/beatmaps/{b}/scores", scores), web.get("/ws", ws), web.get("/_log", get_log), web.route("*", "/{tail:.*}", fallback)])
web.run_app(app, host="127.0.0.1", port=PORT, print=None)
