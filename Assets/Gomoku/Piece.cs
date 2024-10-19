using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Tilemaps;


namespace BoardGames.Gomoku
{
	[CreateAssetMenu(fileName = "New Piece", menuName = "Chess/Gomoku/Piece", order = 0)]
	public sealed class Piece : TileBase
	{
		[field: SerializeField] public Symbol symbol { get; private set; }
		[ShowAssetPreview][SerializeField] private Sprite sprite;


		public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
			=> tileData.sprite = sprite;
	}
}