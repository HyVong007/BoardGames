using RotaryHeart.Lib.SerializableDictionary;
using System;
using UnityEngine;
using UnityEngine.Tilemaps;


namespace BoardGames.Gomoku
{
	public sealed class Board : MonoBehaviour
	{
		private Core core;
		[SerializeField] private Button button;
		[SerializeField] private Tilemap gridMap, pieceMap;
		[SerializeField] private TileBase grid;
		[SerializeField] private SerializableDictionaryBase<Symbol, Piece> pieces;
		private static Board instance;

		private void Awake()
		{
			instance = instance ? throw new Exception() : this;
			"board size".Get(out Vector2Int size);
			core = new(size);
			core.onStateChanged += OnStateChanged;
			button.transform.localScale = size.ToVector3();
			button.click += OnPlayerClick;
			gridMap.origin = pieceMap.origin = default;
			gridMap.size = pieceMap.size = size.ToVector3Int();
			gridMap.FloodFill(default, grid);

			var cam = Camera.main;
			cam.transform.position = new(size.x / 2f, size.y / 2f, -10f);
			cam.aspect = (float)size.x / size.y;
			cam.orthographicSize = size.y / 2f;
		}


		private readonly (int x, int y)[] indexes = new (int x, int y)[4];
		private void Start()
		{
			indexes[0] = (0, 0);
			indexes[1] = (0, (int)core.rect.height - 1);
			indexes[2] = ((int)core.rect.width - 1, (int)core.rect.height - 1);
			indexes[3] = ((int)core.rect.width - 1, 0);

			var s = Symbol.O;
			foreach (var (x, y) in indexes)
			{
				core.Move(MoveType.Play, new(s, new(x, y)));
				pieceMap.SetTile(new(x, y), pieces[s]);
				s = 1 - s;
			}
		}


		private void OnStateChanged(Core.State? state)
		{
			if (state != null) button.interactable = false;
		}


		private Symbol symbol;
		private void OnPlayerClick(Vector2 pixel)
		{
			var index = Camera.main.ScreenToWorldPoint(pixel).ToVector2Int();
			if (core[index.x, index.y] != null) return;

			core.Move(MoveType.Play, new MoveData(symbol, index));
			pieceMap.SetTile(index.ToVector3Int(), pieces[symbol]);
			symbol = 1 - symbol;
		}
	}
}