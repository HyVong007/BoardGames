using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Tilemaps;


namespace BoardGames.GOChess
{
	[CreateAssetMenu(fileName = "New PieceGUI", menuName = "Chess/GO Chess/Piece", order = 1)]
	public sealed class Piece : TileBase
	{
		[field: SerializeField] public Color color { get; private set; }
		[ShowAssetPreview]
		[SerializeField] private Sprite sprite;


		public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
			=> tileData.sprite = sprite;
	}
}