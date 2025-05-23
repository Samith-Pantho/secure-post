import requests

def update_match_result(token: str,  match_id: str, player1_moves: int, player2_moves: int, winner: str):
    url = "http://secure-service-backend:8080/api/UpdateMatchResult"
    token = token

    payload = {
        "MatchId": match_id,
        "Player1Moves": player1_moves,
        "Player2Moves": player2_moves,
        "Winner": winner
    }

    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json"
    }

    print("Payload:", payload)
    try:

        response = requests.post(url, json=payload, headers=headers)
        print("Status Code:", response.status_code)
        print("Response Body:", response.text)
    except (Exception,) as e:
        print(f"Error occurred while making the request {e}")
