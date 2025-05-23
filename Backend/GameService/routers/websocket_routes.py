from fastapi import APIRouter, WebSocket, WebSocketDisconnect, Query

from services.game import handle_match_join
from services.lobby import handle_lobby_socket, handle_user_socket

websocket_router = APIRouter()

@websocket_router.websocket("/ws/lobby")
async def lobby_socket(websocket: WebSocket):
    await handle_lobby_socket(websocket)

@websocket_router.websocket("/ws/user/{user_id}")
async def user_socket(websocket: WebSocket, user_id: str):
    await handle_user_socket(websocket, user_id)

@websocket_router.websocket("/ws/game/{user_id}/{match_id}")
async def game_socket(websocket: WebSocket, user_id: str, match_id: str):
    await handle_match_join(websocket, user_id, match_id)