using Cysharp.Threading.Tasks;
using RotaryHeart.Lib.SerializableDictionary;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace BoardGames.KingChess
{
	[RequireComponent(typeof(Button), typeof(BoxCollider2D))]
	public sealed class Board : MonoBehaviour
	{
		private Button button;
		[Serializable] private sealed class PieceName_Pieces : SerializableDictionaryBase<PieceName, ObjectPool<Piece>> { }
		[SerializeField] private SerializableDictionaryBase<Color, PieceName_Pieces> pieces;
		private Core core;
		private Piece[][] mailBox;
		private static Board instance;

		private void Awake()
		{
			instance = instance ? throw new Exception() : this;
			core = new(); // Test
			MoveData.getPromotedName = async (color, token) => PieceName.Queen; // Test
			mailBox = Util.NewArray(8, 8, (x, y) =>
			{
				var p = core[x, y];
				return p != null ? pieces[p.Value.color][p.Value.name].Get(new(x, y)) : null;
			});

			(button = GetComponent<Button>()).beginDrag += BeginDrag;
		}


		[SerializeField] private Transform cellFlag;
		[SerializeField] private ObjectPool<Transform> hintPool;
		private static readonly List<Vector2Int> moves = new();
		private Color current = Color.White;

		private bool BeginDrag(Vector2 pixel)
		{
			var from = Camera.main.ScreenToWorldPoint(pixel).ToVector2Int();
			if (core[from.x, from.y] == null) return false;

			moves.Clear();
			foreach (var move in core.FindLegalMoves(from))
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
				if (0 <= f.x && f.x < 8 && 0 <= f.y && f.y < 8) cellFlag.position = f;
				pos.x -= 0.5f; pos.y -= 0.5f;
				piece.transform.position = pos;
				return true;
			}


			async void endDrag(Vector2 px)
			{
				hintPool.Recycle();
				button.dragging -= dragging;
				button.endDrag -= endDrag;
				cellFlag.position = new(-1, -1);
				var to = Camera.main.ScreenToWorldPoint(px).ToVector2Int();
				button.interactable = false;
				if (!moves.Contains(to))
				{
					await piece.transform.Move(from.ToVector3(), 0.15f);
					button.interactable = true;
					piece.DecreaseSortOrder();
					return;
				}

				piece.DecreaseSortOrder();
				var data = await MoveData.New(core, from, to);
				await OnPlayerMove(MoveType.Play, data);
				current = 1 - current;
				button.interactable = true;
			}
		}


		[SerializeField] private Transform moveTarget;
		[SerializeField] private float pieceMoveSpeed;
		public async UniTask OnPlayerMove(MoveType mode, MoveData data)
		{
			core.Move(mode, data);

			var from = data.from.ToMailBoxIndex();
			var to = data.to.ToMailBoxIndex();
			if (mode != MoveType.Undo)
			{
				#region DO
				var piece = mailBox[from.x][from.y];
				mailBox[from.x][from.y] = null;

				// Nếu là AI hoặc Remote thì tô màu đích đến trước khi di chuyển quân cờ
				//if (!TurnManager.instance.CurrentPlayerIsLocalHuman()) moveTarget.position = to.ToVector3();

				await piece.transform.Move(to.ToVector3(), pieceMoveSpeed);

				#region Đặt quân vào ô vị trí {to}
				if (data.promotedName != null)
				{
					pieces[piece.color][piece.name].Recycle(piece);
					piece = pieces[data.playerID][data.promotedName.Value].Get(to.ToVector3());
				}

				var enemy = mailBox[to.x][to.y];
				if (enemy) pieces[enemy.color][enemy.name].Recycle(enemy);
				mailBox[to.x][to.y] = piece;
				moveTarget.position = to.ToVector3();
				#endregion

				#region Xử lý nếu có bắt quân đối phương
				if (data.capturedName != null)
					if (data.enpassantCapturedIndex != null)
					{
						var index = data.enpassantCapturedIndex.Value.ToMailBoxIndex();
						enemy = mailBox[index.x][index.y];
						mailBox[index.x][index.y] = null;
						pieces[enemy.color][enemy.name].Recycle(enemy);
					}
				#endregion

				if (data.castling != MoveData.Castling.None)
				{
					var r = Core.CASTLING_ROOK_MOVEMENTS[data.playerID][data.castling];
					var rook = mailBox[r.m_from.x][r.m_from.y];
					mailBox[r.m_from.x][r.m_from.y] = null;
					await rook.transform.Move(r.m_to.ToVector3(), pieceMoveSpeed);
					mailBox[r.m_to.x][r.m_to.y] = rook;
				}
				#endregion
			}
			else
			{
				#region UNDO
				var piece = mailBox[to.x][to.y];

				#region Lấy quân {data.name} hoặc {data.promotedName} ra khỏi ô vị trí {to}
				if (data.promotedName != null)
				{
					pieces[piece.color][piece.name].Recycle(piece);
					piece = pieces[data.playerID][data.name].Get(new(to.x, to.y));
				}
				#endregion

				#region Khôi phục lại quân đối phương bị bắt nếu có
				if (data.capturedName != null)
				{
					if (data.enpassantCapturedIndex != null)
					{
						mailBox[to.x][to.y] = null;
						var index = data.enpassantCapturedIndex.Value.ToMailBoxIndex();
						mailBox[index.x][index.y] = pieces[1 - data.playerID][PieceName.Pawn].Get(index.ToVector3());
					}
					else mailBox[to.x][to.y] = pieces[1 - data.playerID][data.capturedName.Value].Get(to.ToVector3());
				}
				else mailBox[to.x][to.y] = null;
				#endregion

				// Nếu là AI hoặc Remote thì tô màu đích đến trước khi di chuyển quân cờ
				//if (!TurnManager.instance.CurrentPlayerIsLocalHuman()) moveTarget.position = from.ToVector3();
				await piece.transform.Move(from.ToVector3(), pieceMoveSpeed);
				mailBox[from.x][from.y] = piece;

				if (data.castling != MoveData.Castling.None)
				{
					var r = Core.CASTLING_ROOK_MOVEMENTS[data.playerID][data.castling];
					var rook = mailBox[r.m_to.x][r.m_to.y];
					mailBox[r.m_to.x][r.m_to.y] = null;
					await rook.transform.Move(from.ToVector3(), pieceMoveSpeed);
					mailBox[r.m_from.x][r.m_from.y] = rook;
				}
				#endregion
			}
		}
	}
}