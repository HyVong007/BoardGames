using Cysharp.Threading.Tasks;
using RotaryHeart.Lib.SerializableDictionary;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace BoardGames.ChineseChess
{
	public sealed class Board : MonoBehaviour
	{
		private static Board instance;
		[SerializeField] private Button button;

		[Serializable]
		private sealed class PieceName_PieceGUI : SerializableDictionaryBase<PieceName, ObjectPool<PieceGUI>> { }

		[SerializeField] private SerializableDictionaryBase<Color, PieceName_PieceGUI> pieces;

		private Core core;
		private readonly PieceGUI[][] mailBox = new PieceGUI[9][];
		private void Awake()
		{
			instance = instance ? throw new Exception() : this;
			core = new(); // Test
			for (int x = 0; x < 9; ++x)
			{
				mailBox[x] = new PieceGUI[10];
				for (int y = 0; y < 10; ++y)
					if (core[x, y] != null)
					{
						var piece = core[x, y].Value;
						(mailBox[x][y] = pieces[piece.color][piece.name].Get(new(x, y))).hidden = piece.hidden;
					}
			}

			button.beginDrag += BeginDrag;
		}


		[SerializeField] private Transform cellFlag;
		[SerializeField] private ObjectPool<Transform> hintPool;
		private Color current = Color.Red;
		private static readonly List<Vector2Int> moves = new();

		private bool BeginDrag(Vector2 pixel)
		{
			var from = Camera.main.ScreenToWorldPoint(pixel).ToVector2Int();
			if (core[from.x, from.y] == null) return false;

			moves.Clear();
			foreach (var move in core.FindLegalMoves(from.x, from.y))
			{
				moves.Add(move);
				hintPool.Get(move.ToVector3());
			}

			if (moves.Count == 0) return false;

			if (core[from.x, from.y].Value.color != current)
			{
				button.endDrag += _;
				return true;

				void _(Vector2 __)
				{
					hintPool.Recycle();
					button.endDrag -= _;
				}
			}

			var piece = mailBox[from.x][from.y];
			piece.IncreaseSortOrder();
			button.dragging += dragging;
			button.endDrag += endDrag;
			return true;


			bool dragging(Vector2 px)
			{
				var pos = Camera.main.ScreenToWorldPoint(px);
				pos.z = 0;
				var f = Vector3Int.FloorToInt(pos);
				if (Core.InsideBoard(f.x, f.y)) cellFlag.position = f;
				pos.x -= 0.5f; pos.y -= 0.5f;
				piece.transform.position = pos;
				return true;
			}


			async void endDrag(Vector2 px)
			{
				hintPool.Recycle();
				button.dragging -= dragging;
				button.endDrag -= endDrag;
				cellFlag.position = new Vector3(-1, -1);
				var to = Camera.main.ScreenToWorldPoint(px).ToVector2Int();
				button.interactable = false;
				if (!moves.Contains(to))
				{
					await piece.transform.Move(from.ToVector3(), 0.15f);
					piece.DecreaseSortOrder();
					button.interactable = true;
					return;
				}

				piece.DecreaseSortOrder();
				await OnPlayerMove(MoveType.Play, new MoveData(core, from, to));
				current = 1 - current;
				button.interactable = true;
			}
		}


		[SerializeField] private Transform moveTarget;
		[SerializeField] private float pieceMoveSpeed;
		public async UniTask OnPlayerMove(MoveType mode, MoveData data)
		{
			core.Move(mode, data);

			if (mode != MoveType.Undo)
			{
				#region DO
				var piece = mailBox[data.from.x][data.from.y];
				mailBox[data.from.x][data.from.y] = null;

				// Nếu là AI hoặc Remote thì tô màu đích đến trước khi di chuyển quân cờ
				//if (!TurnManager.instance.CurrentPlayerIsLocalHuman()) moveTarget.position = data.to.ToVector3();

				piece.IncreaseSortOrder();
				await piece.transform.Move(data.to.ToVector3(), pieceMoveSpeed);
				piece.DecreaseSortOrder();

				var enemy = mailBox[data.to.x][data.to.y];
				if (enemy) pieces[enemy.color][enemy.name].Recycle(enemy);
				(mailBox[data.to.x][data.to.y] = piece).hidden = false;
				moveTarget.position = data.to.ToVector3();
				#endregion
			}
			else
			{
				#region UNDO
				var piece = mailBox[data.to.x][data.to.y];
				if (data.capturedPiece != null)
				{
					var enemy = data.capturedPiece.Value;
					(mailBox[data.to.x][data.to.y] = pieces[enemy.color][enemy.name].Get(data.to.ToVector3()))
						.hidden = enemy.hidden;
				}
				else mailBox[data.to.x][data.to.y] = null;

				piece.IncreaseSortOrder();
				await piece.transform.Move(data.from.ToVector3(), pieceMoveSpeed);
				piece.DecreaseSortOrder();
				(mailBox[data.from.x][data.from.y] = piece).hidden = core[data.from.x, data.from.y].Value.hidden;
				moveTarget.position = data.from.ToVector3();
				#endregion
			}
		}
	}
}