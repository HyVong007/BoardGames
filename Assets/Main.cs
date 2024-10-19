using UnityEngine;


namespace BoardGames
{
	public sealed class Main : MonoBehaviour
	{
		private void Awake()
		{
			"board size".Set(new Vector2Int(20, 10));
		}
	}
}