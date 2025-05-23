from fastapi import HTTPException
from starlette.websockets import WebSocket, WebSocketDisconnect

from services.commands import request_match_command, request_join_match
from services.common import notify_user
from services.data import game_data, match_players
from services.game_manager import GameManager
from services.models import MatchCreateRequest, MatchAcceptRequest, Game, GameStatus, GameState, GameProcess


async def match_request_notifier(payload: MatchCreateRequest):
    await notify_user(payload.Player2, request_match_command({
        "status": payload.MatchStatus,
        "requested_by": payload.Player1,
        "match_id": payload.MatchId,
    }))


async def create_game(payload: MatchAcceptRequest):
    game = Game(
        matchId=payload.MatchId,
        player1=payload.Player1,
        player2=payload.Player2,
        player1SecretKey=payload.Player1SecretKey,
        player2SecretKey=payload.Player2SecretKey,
        status=GameStatus.WAITING,
        matchToken=payload.MatchToken,
        currentState=GameState(
            currentTurn=payload.FirstTurn,
            currentProcess=GameProcess.ROLLING,
        )
    )

    game_manager = GameManager(
        game=game,
    )
    game_data[payload.MatchId] = game_manager


    for user in [payload.Player1, payload.Player2]:
        await notify_user(user, request_join_match({
            "requested_by": payload.Player1 if user == payload.Player2 else payload.Player2,
            "match_id": payload.MatchId,
        }))



async def handle_match_join(websocket: WebSocket, user_id: str, match_id):
    await websocket.accept()
    players = match_players.get(match_id, {})

    if user_id in players:
        return
    players.update({
        f"{user_id}": websocket
    })
    match_players[match_id] = players

    print(f"{user_id} connected to game socket and joined lobby.")
    if len(players.keys()) == 2:
        game = game_data.get(match_id)
        if not game:
            raise ValueError("Game not found")
        game.match_players = players
        await game.next_turn()
        game_data[match_id] = game
    try:
        while True:
            data = await websocket.receive_json()
            print(f"{user_id} says: {data}")
    except WebSocketDisconnect:
        print(f"{user_id} disconnected.")
        # TODO: handle disconnection

        # active_websockets.pop(user_id, None)
        # if user_id in lobby_users:
        #     lobby_users.remove(user_id)
        #     await broadcast_lobby_update()

def verify_user_permission(game_manager: GameManager, token: str, user_id: str):
    expected_token = game_manager.game.player1SecretKey if user_id == game_manager.game.player1 else game_manager.game.player2SecretKey
    if token != expected_token:
        raise HTTPException(status_code=403, detail="Invalid token")

async def handle_game_roll_dice(user_id: str, match_id: str, roll: int, token: str):
    game_manager = game_data.get(match_id)
    verify_user_permission(game_manager, token, user_id)
    if not game_manager:
        raise HTTPException(status_code=404, detail="Game not found")
    if user_id != game_manager.game.currentState.currentTurn:
        raise HTTPException(status_code=403, detail="Not your turn")

    game_manager.game.currentState.roll = roll
    await game_manager.next_turn()
    game_data[match_id] = game_manager


async def handle_game_claim_dice(user_id: str, match_id: str, claim: int, token: str):
    game_manager = game_data.get(match_id)

    expected_user = game_manager.game.player1 if game_manager.game.currentState.currentTurn == game_manager.game.player2 else game_manager.game.player2
    verify_user_permission(game_manager, token, expected_user)
    if not game_manager:
        raise HTTPException(status_code=404, detail="Game not found")
    if user_id != expected_user:
        raise HTTPException(status_code=403, detail="Not your turn")

    game_manager.game.currentState.claim = claim
    await game_manager.next_turn()
    game_data[match_id] = game_manager

async def handle_game_round_decide(user_id: str, match_id: str, decision: bool, token: str):
    game_manager = game_data.get(match_id)

    verify_user_permission(game_manager, token, user_id)
    if not game_manager:
        raise HTTPException(status_code=404, detail="Game not found")
    if user_id != game_manager.game.currentState.currentTurn:
        raise HTTPException(status_code=403, detail="Not your turn")

    game_manager.game.currentState.decide = decision
    await game_manager.next_turn()
    game_data[match_id] = game_manager
