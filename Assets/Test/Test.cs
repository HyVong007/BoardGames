using BoardGames.KingChess;
using System.Linq;
using UnityEngine;


public class Test : MonoBehaviour
{
	private void Awake()
	{
		var core = new BoardGames.KingChess.Core();
		//print(core.FindLegalMoves(new Vector2Int(0, 1)).Any());
	}
}
