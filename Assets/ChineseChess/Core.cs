using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;


namespace BoardGames.ChineseChess
{
	public enum Color
	{
		Red = 0, Black = 1
	}



	public enum PieceName
	{
		General = 0,
		Advisor = 1,
		Elephant = 2,
		Horse = 3,
		Rook = 4,
		Cannon = 5,
		Pawn = 6
	}



	public readonly struct Piece
	{
		public readonly Color color;
		public readonly PieceName name;
		public readonly bool hidden;


		public Piece(Color color, PieceName name, bool hidden = false)
		{
#if DEBUG
			if (name == PieceName.General && hidden)
				throw new InvalidOperationException("Tướng không thể úp !");
#endif
			this.color = color;
			this.name = name;
			this.hidden = hidden;
		}


		public override string ToString() => $"(color= {color}, name= {name}, hidden= {hidden})";
	}



	public readonly struct MoveData : IMoveData<Color>
	{
		public Color playerID => piece.color;
		public readonly Piece piece;
		public readonly Vector2Int from, to;
		public readonly Piece? capturedPiece;


		public MoveData(in Piece piece, in Vector2Int from, in Vector2Int to, in Piece? capturedPiece)
		{
			this.piece = piece;
			this.from = from;
			this.to = to;
			this.capturedPiece = capturedPiece;
		}


		public MoveData(Core core, in Vector2Int from, in Vector2Int to) :
		this(core[from.x, from.y].Value, from, to, core[to.x, to.y])
		{ }


		public override string ToString() => $"(piece= {piece}, from= {from}, to= {to}, capturedPiece= {capturedPiece})";
	}



	public sealed class Core
	{
		#region Khai báo dữ liệu và khởi tạo
		private readonly Piece?[][] mailBox;
		private static readonly Piece?[][] DEFAULT_MAILBOX = new Piece?[][]
		{
			// FILE A
			new Piece?[] { new (Color.Red, PieceName.Rook), null, null, new (Color.Red, PieceName.Pawn), null, null, new (Color.Black, PieceName.Pawn), null, null, new (Color.Black, PieceName.Rook) },

			// FILE B
			new Piece?[] { new (Color.Red, PieceName.Horse), null, new (Color.Red, PieceName.Cannon), null, null, null, null, new (Color.Black, PieceName.Cannon), null, new (Color.Black, PieceName.Horse) },

			// FILE C
			new Piece?[] { new (Color.Red, PieceName.Elephant), null, null, new (Color.Red, PieceName.Pawn), null, null, new (Color.Black, PieceName.Pawn), null, null, new (Color.Black, PieceName.Elephant) },

			// FILE D
			new Piece?[] { new (Color.Red, PieceName.Advisor), null, null, null, null, null, null, null, null, new (Color.Black, PieceName.Advisor) },

			// FILE E
			new Piece?[] { new (Color.Red, PieceName.General), null, null, new (Color.Red, PieceName.Pawn), null, null, new (Color.Black, PieceName.Pawn), null, null, new (Color.Black, PieceName.General) },

			// FILE F
			new Piece?[] { new (Color.Red, PieceName.Advisor), null, null, null, null, null, null, null, null, new (Color.Black, PieceName.Advisor) },

			// FILE G
			new Piece?[] { new (Color.Red, PieceName.Elephant), null, null, new (Color.Red, PieceName.Pawn), null, null, new (Color.Black, PieceName.Pawn), null, null, new (Color.Black, PieceName.Elephant) },

			// FILE H
			new Piece?[] { new (Color.Red, PieceName.Horse), null, new (Color.Red, PieceName.Cannon), null, null, null, null, new (Color.Black, PieceName.Cannon), null, new (Color.Black, PieceName.Horse) },

			// FILE I
			new Piece?[] { new (Color.Red, PieceName.Rook), null, null, new (Color.Red, PieceName.Pawn), null, null, new (Color.Black, PieceName.Pawn), null, null, new (Color.Black, PieceName.Rook) }
		};
		/// <summary>
		/// if <see langword="true"/> : Chơi theo luật Cờ Úp
		/// </summary>
		private readonly bool hiddenChessRule;


		private static readonly (int x, int y)[] ALL_BOARD_INDEXS = new (int x, int y)[90];
		static Core()
		{
			for (int index = 0, x = 0; x < 9; ++x)
				for (int y = 0; y < 10; ++y)
					ALL_BOARD_INDEXS[index++] = (x, y);
		}


		public Core(Piece?[][] mailBox = null)
		{
#if DEBUG
			if (mailBox != null && (mailBox.Length != 9 || mailBox[0].Length != 10))
				throw new ArgumentOutOfRangeException("mailBox phải là 9x10 !");
#endif
			mailBox ??= DEFAULT_MAILBOX;
			bool hidden = false;
			this.mailBox = Util.NewArray<Piece?>(9, 10, (x, y) =>
			{
				if (mailBox[x][y] != null)
				{
					var piece = mailBox[x][y].Value;
					hidden |= piece.hidden;
					if (piece.name == PieceName.General) generalIndexes[piece.color] = new(x, y);
					return piece;
				}

				return null;
			});

			hiddenChessRule = hidden;
		}


		public Core(Core core) : this(core.mailBox)
		{
			if (GetState(Color.Red) == State.CheckMate || GetState(Color.Black) == State.CheckMate)
				throw new InvalidOperationException("Không thể copy Board đã kết thúc !");
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Piece?[][] CloneDefaultMailBox()
			=> Util.NewArray(9, 10, (x, y) => DEFAULT_MAILBOX[x][y]);


		public Piece? this[int x, int y] => mailBox[x][y];
		public Piece? this[Vector2Int index] => mailBox[index.x][index.y];
		#endregion


		#region State
		public enum State
		{
			Normal, Check, CheckMate
		}
		private readonly Dictionary<Color, State> states = new()
		{
			[Color.Red] = State.Normal,
			[Color.Black] = State.Normal
		};
		public event Action<Color, State> onStateChanged;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public State GetState(Color color) => states[color];


		private readonly Dictionary<Color, Vector2Int> generalIndexes = new(2);
		private bool GeneralIsChecked(Color color)
		{
			var G = generalIndexes[color];
			var enemyColor = 1 - color;

			#region Kiểm tra lộ mặt Tướng
			var enemyG = generalIndexes[enemyColor];
			if (G.x == enemyG.x)
			{
				var m = mailBox[G.x];
				int DIR_Y = COLOR_FORWARD_VECTORS[color].y;
				while (true)
				{
					if (m[G.y += DIR_Y] != null || m[enemyG.y -= DIR_Y] != null) break;
					if (Mathf.Abs(G.y - enemyG.y) == 1) return true;
				}
			}
			#endregion

			G = generalIndexes[color];
			foreach (var (x, y) in ALL_BOARD_INDEXS.Random())
				if (mailBox[x][y]?.color == enemyColor)
				{
					var piece = mailBox[x][y].Value;
					if (piece.name == PieceName.General) continue;

					foreach (var move in FindPseudoLegalMoves(enemyColor, piece.hidden ? DEFAULT_MAILBOX[x][y].Value.name : piece.name, new(x, y)))
						if (move == G) return true;
				}

			return false;
		}
		#endregion


		#region DEBUG
		public override string ToString()
		{
			string s = "";
			for (int y = 9; y >= 0; --y)
			{
				s += $"{y + 1}	";
				for (int x = 0; x < 9; ++x)
				{
					if (mailBox[x][y] == null) { s += "   *   "; continue; }
					var square = mailBox[x][y].Value;
					s += $"   {(square.hidden ? "?" : (square.color == Color.Red ? PIECENAME_STRING[square.name] : PIECENAME_STRING[square.name].ToLower()))}   ";
				}
				s += "\n\n\n";
			}
			return s + "\n\n	   A      B      C      D      E      F      G      H      I";
		}


		public void Print()
		{
			for (int y = 9; y >= 0; --y)
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write($"{y + 1}	");
				for (int x = 0; x < 9; ++x)
				{
					if (mailBox[x][y] == null)
					{
						Console.ForegroundColor = ConsoleColor.DarkYellow;
						Console.Write("   *   ");
						continue;
					}

					var square = mailBox[x][y].Value;
					if (square.hidden)
					{
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write("   ?   ");
					}
					else
					{
						Console.ForegroundColor = square.color == Color.Red ? ConsoleColor.Red : ConsoleColor.Blue;
						Console.Write($"   {PIECENAME_STRING[square.name]}   ");
					}
				}
				Console.WriteLine("\n\n");
			}
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("\n\n	   A      B      C      D      E      F      G      H      I");
			Console.ForegroundColor = ConsoleColor.White;
		}


		private static readonly IReadOnlyDictionary<PieceName, string> PIECENAME_STRING = new Dictionary<PieceName, string>
		{
			[PieceName.General] = "G",
			[PieceName.Advisor] = "A",
			[PieceName.Elephant] = "E",
			[PieceName.Horse] = "H",
			[PieceName.Rook] = "R",
			[PieceName.Cannon] = "C",
			[PieceName.Pawn] = "P"
		};
		#endregion


		#region FindPseudoLegalMoves
		private static readonly IReadOnlyDictionary<Color, Rect> SIDES = new Dictionary<Color, Rect>
		{
			[Color.Red] = new Rect(0, 0, 9, 5),
			[Color.Black] = new Rect(0, 5, 9, 5)
		}, PALACES = new Dictionary<Color, Rect>
		{
			[Color.Red] = new Rect(3, 0, 3, 3),
			[Color.Black] = new Rect(3, 7, 3, 3)
		};

		private static readonly (int x, int y)[] DPAD_VECTORS = new (int x, int y)[]
		{
			(-1, 0), (1, 0), (0, 1), (0, -1)
		},
			CROSS_VECTORS = new (int x, int y)[]
		{
			(-1, -1), (1, 1), (-1, 1), (1, -1)
		};
		private static readonly ((int x, int y) line, (int x, int y)[] crosses)[] HORSE_VECTORS = new ((int x, int y) line, (int x, int y)[] crosses)[]
		{
			(line: (-1, 0), crosses: new (int x, int y)[]{(-1, 1), (-1, -1)}),	// L
			(line: (1, 0), crosses: new (int x, int y)[]{(1, 1), (1, -1)}),		// R
			(line: (0, 1), crosses: new (int x, int y)[]{(-1, 1), (1, 1)}),		// U
			(line: (0, -1), crosses: new (int x, int y)[]{(-1, -1), (1, -1)})   // D
		};
		private static readonly IReadOnlyDictionary<Color, (int x, int y)> COLOR_FORWARD_VECTORS = new Dictionary<Color, (int x, int y)>
		{
			[Color.Red] = (0, 1),
			[Color.Black] = (0, -1)
		};

		private IEnumerable<Vector2Int> FindPseudoLegalMoves(Color color, PieceName name, Vector2Int index)
		{
			var THIS_SIDE = SIDES[color];
			var THIS_PALACE = PALACES[color];
			int x, y;

			switch (name)
			{
				case PieceName.General:
					#region General
					for (int d = 0; d < 4; ++d)
					{
						x = index.x + DPAD_VECTORS[d].x; y = index.y + DPAD_VECTORS[d].y;
						if (THIS_PALACE.Contains(x, y) && mailBox[x][y]?.color != color) yield return new(x, y);
					}
					break;
				#endregion

				case PieceName.Advisor:
					#region Advisor
					for (int d = 0; d < 4; ++d)
					{
						x = index.x + CROSS_VECTORS[d].x; y = index.y + CROSS_VECTORS[d].y;
						if (((!hiddenChessRule && THIS_PALACE.Contains(x, y))
							|| (hiddenChessRule && InsideBoard(x, y)))
							&& mailBox[x][y]?.color != color) yield return new(x, y);
					}
					break;
				#endregion

				case PieceName.Elephant:
					#region Elephant
					for (int d = 0; d < 4; ++d)
					{
						var dir = CROSS_VECTORS[d];
						x = index.x + dir.x; y = index.y + dir.y;
						if ((!hiddenChessRule && !THIS_SIDE.Contains(x, y))
							|| (hiddenChessRule && !InsideBoard(x, y))
							|| mailBox[x][y] != null) continue;

						x += dir.x; y += dir.y;
						if (((!hiddenChessRule && THIS_SIDE.Contains(x, y))
							|| (hiddenChessRule && InsideBoard(x, y)))
							&& mailBox[x][y]?.color != color) yield return new(x, y);
					}
					break;
				#endregion

				case PieceName.Horse:
					#region Horse
					for (int h = 0; h < 4; ++h)
					{
						var (line, crosses) = HORSE_VECTORS[h];
						x = index.x + line.x; y = index.y + line.y;
						if (!InsideBoard(x, y) || mailBox[x][y] != null) continue;

						for (int c = 0; c < 2; ++c)
						{
							int cx = x + crosses[c].x, cy = y + crosses[c].y;
							if (InsideBoard(cx, cy) && mailBox[cx][cy]?.color != color) yield return new(cx, cy);
						}
					}
					break;
				#endregion

				case PieceName.Rook:
					#region Rook
					for (int d = 0; d < 4; ++d)
					{
						var dir = DPAD_VECTORS[d];
						(x, y) = (index.x, index.y);
						while (InsideBoard(x += dir.x, y += dir.y))
						{
							var c = mailBox[x][y]?.color;
							if (c != color) yield return new(x, y);
							if (c != null) break;
						}
					}
					break;
				#endregion

				case PieceName.Cannon:
					#region Cannon
					for (int d = 0; d < 4; ++d)
					{
						var dir = DPAD_VECTORS[d];
						(x, y) = (index.x, index.y);
						while (InsideBoard(x += dir.x, y += dir.y))
						{
							var c = mailBox[x][y]?.color;
							if (c == null)
							{
								yield return new(x, y); continue;
							}

							while (InsideBoard(x += dir.x, y += dir.y))
							{
								var c2 = mailBox[x][y]?.color;
								if (c2 == null) continue;

								if (c2 != color) yield return new(x, y);
								break;
							}
							break;
						}
					}
					break;
				#endregion

				case PieceName.Pawn:
					#region Pawn
					var forward = COLOR_FORWARD_VECTORS[color];
					x = index.x + forward.x; y = index.y + forward.y;
					if (InsideBoard(x, y) && mailBox[x][y]?.color != color) yield return new(x, y);
					if (THIS_SIDE.Contains(index)) break;

					for (int d = 0; d < 2; ++d)
					{
						x = index.x + DPAD_VECTORS[d].x; y = index.y + DPAD_VECTORS[d].y;
						if (InsideBoard(x, y) && mailBox[x][y]?.color != color) yield return new(x, y);
					}
					break;
					#endregion
			}
		}
		#endregion


		#region FindLegalMoves
		private IEnumerable<Vector2Int> FindLegalMoves(Color color, PieceName name, Vector2Int index)
		{
			foreach (var to in FindPseudoLegalMoves(color, name, index))
			{
				var data = new MoveData(this, index, to);
				PseudoMove(undo: false, data);
				bool check = false;
				if (GeneralIsChecked(color)) check = true;
				PseudoMove(undo: true, data);
				if (!check) yield return to;
			}
		}


		public IEnumerable<Vector2Int> FindLegalMoves(int x, int y)
		{
			var piece = mailBox[x][y].Value;
			return FindLegalMoves(piece.color, !piece.hidden ? piece.name : DEFAULT_MAILBOX[x][y].Value.name, new Vector2Int(x, y));
		}
		#endregion


		#region Move
		public void Move(MoveType mode, in MoveData data)
		{
			bool undo = mode == MoveType.Undo;
			PseudoMove(undo, data);

			#region Cập nhật State
			var enemyColor = 1 - data.piece.color;
			if (!undo)
			{
				#region DO
				var oldState = states[data.piece.color];
				states[data.piece.color] = State.Normal;
				if (oldState == State.Check) onStateChanged?.Invoke(data.piece.color, State.Normal);

				foreach (var (x, y) in ALL_BOARD_INDEXS.Random())
					if (mailBox[x][y]?.color == enemyColor)
					{
						var piece = mailBox[x][y].Value;
						if (FindLegalMoves(enemyColor, !piece.hidden ? piece.name : DEFAULT_MAILBOX[x][y].Value.name, new(x, y)).Any())
						{
							if (GeneralIsChecked(enemyColor))
							{
								states[enemyColor] = State.Check;
								onStateChanged?.Invoke(enemyColor, State.Check);
							}

							goto FINISH_UPDATING_STATE;
						}
					}

				states[enemyColor] = State.CheckMate;
				onStateChanged?.Invoke(enemyColor, State.CheckMate);
				#endregion
			}
			else
			{
				#region UNDO
				var oldState = states[enemyColor];
				states[enemyColor] = State.Normal;
				if (oldState != State.Normal) onStateChanged?.Invoke(enemyColor, State.Normal);

				if (GeneralIsChecked(data.piece.color))
				{
					states[data.piece.color] = State.Check;
					onStateChanged?.Invoke(data.piece.color, State.Check);
				}
				#endregion
			}

		FINISH_UPDATING_STATE:;
			#endregion
		}


		private void PseudoMove(bool undo, in MoveData data)
		{
			if (!undo)
			{
				#region DO
				mailBox[data.from.x][data.from.y] = null;
				mailBox[data.to.x][data.to.y] = new(data.piece.color, data.piece.name);
				if (data.piece.name == PieceName.General) generalIndexes[data.piece.color] = data.to;
				#endregion
			}
			else
			{
				#region UNDO
				mailBox[data.from.x][data.from.y] = data.piece;
				mailBox[data.to.x][data.to.y] = data.capturedPiece;
				if (data.piece.name == PieceName.General) generalIndexes[data.piece.color] = data.from;
				#endregion
			}
		}
		#endregion


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool InsideBoard(int x, int y) => -1 < x && x < 9 && -1 < y && y < 10;
	}



	public static class Extensions
	{
		public static void PrintColorBits(this (int x, int y)[] array)
		{
			for (int y = 9; y >= 0; --y)
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write($"{y + 1}	");
				for (int x = 0; x < 9; ++x)
					if (array.Contains((x, y)))
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.Write("   1   ");
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.White;
						Console.Write("   0   ");
					}
				Console.WriteLine("\n\n");
			}
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("\n\n	   A      B      C      D      E      F      G      H      I");
			Console.ForegroundColor = ConsoleColor.White;
		}
	}
}