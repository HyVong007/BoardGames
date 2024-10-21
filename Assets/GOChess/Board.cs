using RotaryHeart.Lib.SerializableDictionary;
using System;
using UnityEngine;
using UnityEngine.Tilemaps;


namespace BoardGames.GOChess
{
	public sealed class Board : MonoBehaviour
	{
		[SerializeField] private Button button;
		[SerializeField] private Tilemap backgroundMap, gridMap, pieceMap;
		[SerializeField] private TileBase backgroundTile, gridTile;
		[SerializeField] private SerializableDictionaryBase<Color, Piece> pieceTiles;
		private Core core;
		private static Board instance;

		private void Awake()
		{
			instance = instance ? throw new Exception() : this;
			"board size".Get(out Vector2Int size);
			core = new(size);
			var rect = core.rect;
			core.drawPiece += (index, color) => pieceMap.SetTile(index, pieceTiles[color]);
			core.clearPiece += index => pieceMap.SetTile(index, null);
			backgroundMap.size = gridMap.size = new((int)rect.width - 1, (int)rect.height - 1);
			pieceMap.size = new((int)rect.width, (int)rect.height);
			backgroundMap.origin = gridMap.origin = pieceMap.origin = default;
			backgroundMap.FloodFill(default, backgroundTile);
			gridMap.FloodFill(default, gridTile);
			button.transform.localScale = new(rect.width, rect.height);
			button.transform.localPosition = new(rect.width / 2f, rect.height / 2f);
			button.click += OnPlayerClick;
			var cam = Camera.main;
			cam.transform.position = new(rect.width / 2f, rect.height / 2f, -10);
			cam.aspect = rect.width / rect.height;
			cam.orthographicSize = rect.height / 2f;
		}


		private Color color = Color.White;
		[SerializeField] private Transform flag;

		private void OnPlayerClick(Vector2 pixel)
		{
			var index = Camera.main.ScreenToWorldPoint(pixel).ToVector2Int();
			if (!core.CanMove(color, index)) return;

			core.Move(MoveType.Play, new(core, color, index));
			pieceMap.SetTile(index.ToVector3Int(), pieceTiles[color]);
			flag.position = index.ToVector3();
			color = 1 - color;
		}
	}
}