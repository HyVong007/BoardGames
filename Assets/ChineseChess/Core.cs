using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using UnityEngine;
using History = BoardGames.History<BoardGames.ChineseChess.Color, BoardGames.ChineseChess.MoveData>;


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


	[DataContract]
	public readonly struct Piece
	{
		[DataMember] public readonly Color color;
		[DataMember] public readonly PieceName name;
		[DataMember] public readonly bool hidden;


		internal Piece(Color color, PieceName name, bool hidden = false)
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
		private readonly Piece?[][] mailBox = new Piece?[9][];
		private static readonly Piece?[][] DEFAULT_MAILBOX = new Piece?[][]
		{
			// FILE A
			new Piece?[]{new Piece(Color.Red, PieceName.Rook),null, null, new Piece(Color.Red,PieceName.Pawn), null, null, new Piece(Color.Black, PieceName.Pawn), null, null, new Piece(Color.Black, PieceName.Rook) },

			// FILE B
			new Piece?[]{ new Piece( Color.Red,PieceName.Horse ), null, new Piece( Color.Red,  PieceName.Cannon ), null, null, null, null, new Piece( Color.Black,  PieceName.Cannon ), null, new Piece( Color.Black,  PieceName.Horse ) },

			// FILE C
			new Piece?[]{ new Piece( Color.Red,  PieceName.Elephant ), null, null, new Piece( Color.Red, PieceName.Pawn ), null, null, new Piece( Color.Black,  PieceName.Pawn ), null, null, new Piece( Color.Black, PieceName.Elephant ) },

			// FILE D
			new Piece?[]{ new Piece(Color.Red,  PieceName.Advisor ), null, null, null, null, null, null, null, null, new Piece( Color.Black, PieceName.Advisor )},

			// FILE E
			new Piece?[]{ new Piece( Color.Red,  PieceName.General ), null, null, new Piece(Color.Red,  PieceName.Pawn ), null, null, new Piece( Color.Black,  PieceName.Pawn ), null, null, new Piece( Color.Black, PieceName.General )},

			// FILE F
			new Piece?[]{ new Piece( Color.Red,  PieceName.Advisor ), null, null, null, null, null, null, null, null, new Piece( Color.Black,  PieceName.Advisor ) },

			// FILE G
			new Piece?[]{ new Piece( Color.Red, PieceName.Elephant ), null, null, new Piece( Color.Red,  PieceName.Pawn ), null, null, new Piece(Color.Black, PieceName.Pawn ), null, null, new Piece( Color.Black, PieceName.Elephant ) },

			// FILE H
			new Piece?[]{ new Piece( Color.Red, PieceName.Horse ), null, new Piece( Color.Red,  PieceName.Cannon ), null, null, null, null, new Piece( Color.Black, PieceName.Cannon ), null, new Piece( Color.Black,  PieceName.Horse ) },

			// FILE I
			new Piece?[]{new Piece(Color.Red, PieceName.Rook),null, null, new Piece( Color.Red, PieceName.Pawn), null, null, new Piece( Color.Black, PieceName.Pawn), null, null, new Piece( Color.Black, PieceName.Rook) },
		};
		/// <summary>
		/// if <see langword="true"/> : Chơi theo luật Cờ Úp
		/// </summary>
		private readonly bool hiddenChessRule;


		public Core(Piece?[][] mailBox = null)
		{
			if (mailBox != null && (mailBox.Length != 9 || mailBox[0].Length != 10))
				throw new ArgumentOutOfRangeException("mailBox phải là 9x10 !");

			mailBox ??= DEFAULT_MAILBOX;
			for (int x = 0; x < 9; ++x)
			{
				this.mailBox[x] = new Piece?[10];
				for (int y = 0; y < 10; ++y)
					if (mailBox[x][y] != null)
					{
						var piece = (this.mailBox[x][y] = mailBox[x][y]).Value;
						hiddenChessRule |= piece.hidden;
						if (piece.name == PieceName.General) generalIndexes[piece.color] = new Vector2Int(x, y);
					}
			}
		}


		public Core(Core core) : this(core.mailBox)
		{
			if (GetState(Color.Red) == State.CheckMate || GetState(Color.Black) == State.CheckMate)
				throw new InvalidOperationException("Không thể copy Board đã kết thúc !");
		}


		public static Piece?[][] CloneDefaultMailBox()
		{
			var result = new Piece?[9][];
			for (int x = 0; x < 9; ++x)
			{
				result[x] = new Piece?[10];
				for (int y = 0; y < 10; ++y) result[x][y] = DEFAULT_MAILBOX[x][y];
			}
			return result;
		}


		public Piece? this[int x, int y] => mailBox[x][y];
		public Piece? this[Vector2Int index] => mailBox[index.x][index.y];
		#endregion


		#region State
		public enum State
		{
			Normal, Check, CheckMate
		}

		private readonly Dictionary<Color, State> states = new Dictionary<Color, State>
		{
			[Color.Red] = State.Normal,
			[Color.Black] = State.Normal
		};
		public event Action<Color, State> onStateChanged;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public State GetState(Color color) => states[color];

		private readonly Dictionary<Color, Vector2Int> generalIndexes = new Dictionary<Color, Vector2Int>(2);
		private bool GeneralIsChecked(Color color)
		{
			var G = generalIndexes[color];
			var opponentColor = color.Opponent();

			#region Kiểm tra lộ mặt Tướng
			var OpponentG = generalIndexes[opponentColor];
			if (G.x == OpponentG.x)
			{
				var m = mailBox[G.x];
				int DIR_Y = COLOR_FORWARD_VECTORS[color].y;
				while (true)
				{
					if ((G.y += DIR_Y) == OpponentG.y) return true;
					if (m[G.y] != null) break;
				}
			}
			#endregion

			G = generalIndexes[color];
			for (int x = 0; x < 9; ++x)
				for (int y = 0; y < 10; ++y)
					if (mailBox[x][y]?.color == opponentColor)
					{
						var piece = mailBox[x][y].Value;
						if (piece.name == PieceName.General) continue;
						if (FindPseudoLegalMoves(opponentColor, !piece.hidden ? piece.name : DEFAULT_MAILBOX[x][y].Value.name, new Vector2Int(x, y)).Contains(G)) return true;
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
					s += $"   {(square.hidden ? "?" : (square.color == Color.Red ? PIECENAME_STRING_DICT[square.name] : PIECENAME_STRING_DICT[square.name].ToLower()))}   ";
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
						Console.Write($"   {PIECENAME_STRING_DICT[square.name]}   ");
					}
				}
				Console.WriteLine("\n\n");
			}
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("\n\n	   A      B      C      D      E      F      G      H      I");
			Console.ForegroundColor = ConsoleColor.White;
		}


		private static readonly IReadOnlyDictionary<PieceName, string> PIECENAME_STRING_DICT = new Dictionary<PieceName, string>
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
			[Color.Black] = new Rect(0, 5, 9, 10)
		}, PALACES = new Dictionary<Color, Rect>
		{
			[Color.Red] = new Rect(3, 0, 6, 3),
			[Color.Black] = new Rect(3, 7, 6, 10)
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
		private readonly List<Vector2Int> pseudoList = new(90);


		private Vector2Int[] FindPseudoLegalMoves(in Color color, in PieceName name, in Vector2Int index)
		{
			var THIS_SIDE = SIDES[color];
			var THIS_PALACE = PALACES[color];
			pseudoList.Clear();
			int x, y;

			switch (name)
			{
				case PieceName.General:
					#region General
					for (int d = 0; d < 4; ++d)
					{
						x = index.x + DPAD_VECTORS[d].x; y = index.y + DPAD_VECTORS[d].y;
						if (THIS_PALACE.Contains(x, y) && mailBox[x][y]?.color != color) pseudoList.Add(new Vector2Int(x, y));
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
							&& mailBox[x][y]?.color != color) pseudoList.Add(new Vector2Int(x, y));
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
							&& mailBox[x][y]?.color != color) pseudoList.Add(new Vector2Int(x, y));
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
							if (InsideBoard(cx, cy) && mailBox[cx][cy]?.color != color) pseudoList.Add(new Vector2Int(cx, cy));
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
							if (c != color) pseudoList.Add(new Vector2Int(x, y));
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
								pseudoList.Add(new Vector2Int(x, y)); continue;
							}

							while (InsideBoard(x += dir.x, y += dir.y))
							{
								var c2 = mailBox[x][y]?.color;
								if (c2 == null) continue;

								if (c2 != color) pseudoList.Add(new Vector2Int(x, y));
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
					if (InsideBoard(x, y) && mailBox[x][y]?.color != color) pseudoList.Add(new Vector2Int(x, y));
					if (THIS_SIDE.Contains(index)) break;
					for (int d = 0; d < 2; ++d)
					{
						x = index.x + DPAD_VECTORS[d].x; y = index.y + DPAD_VECTORS[d].y;
						if (InsideBoard(x, y) && mailBox[x][y]?.color != color) pseudoList.Add(new Vector2Int(x, y));
					}
					break;
					#endregion
			}
			return pseudoList.ToArray();
		}
		#endregion


		#region FindLegalMoves
		private readonly List<Vector2Int> legalList = new List<Vector2Int>(90);

		private Vector2Int[] FindLegalMoves(in Color color, in PieceName name, in Vector2Int index)
		{
			var moves = FindPseudoLegalMoves(color, name, index);
			if (moves.Length == 0) return Array.Empty<Vector2Int>();

			legalList.Clear();
			for (int m = 0; m < moves.Length; ++m)
			{
				var to = moves[m];
				var data = new MoveData(this, index, to);
				PseudoMove(data, undo: false);
				if (!GeneralIsChecked(color)) legalList.Add(to);
				PseudoMove(data, undo: true);
			}
			return legalList.ToArray();
		}


		public Vector2Int[] FindLegalMoves(in int x, in int y)
		{
			var piece = mailBox[x][y].Value;
			return FindLegalMoves(piece.color, !piece.hidden ? piece.name : DEFAULT_MAILBOX[x][y].Value.name, new Vector2Int(x, y));
		}
		#endregion


		#region Move
		public void Move(MoveData data, History.Mode mode)
		{
			bool undo = mode == History.Mode.Undo;
			PseudoMove(data, undo);

			#region Cập nhật State
			var opponentColor = data.piece.color.Opponent();
			if (!undo)
			{
				#region DO
				var oldState = states[data.piece.color];
				states[data.piece.color] = State.Normal;
				if (oldState == State.Check) onStateChanged?.Invoke(data.piece.color, State.Normal);

				for (int x = 0; x < 9; ++x)
					for (int y = 0; y < 10; ++y)
						if (mailBox[x][y]?.color == opponentColor)
						{
							var piece = mailBox[x][y].Value;
							if (FindLegalMoves(opponentColor, !piece.hidden ? piece.name : DEFAULT_MAILBOX[x][y].Value.name, new Vector2Int(x, y)).Length != 0)
							{
								if (GeneralIsChecked(opponentColor))
								{
									states[opponentColor] = State.Check;
									onStateChanged?.Invoke(opponentColor, State.Check);
								}
								goto FINISH_UPDATING_STATE;
							}
						}

				states[opponentColor] = State.CheckMate;
				onStateChanged?.Invoke(opponentColor, State.CheckMate);
				#endregion
			}
			else
			{
				#region UNDO
				var oldState = states[opponentColor];
				states[opponentColor] = State.Normal;
				if (oldState != State.Normal) onStateChanged?.Invoke(opponentColor, State.Normal);

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


		private void PseudoMove(MoveData data, bool undo)
		{
			if (!undo)
			{
				#region DO
				mailBox[data.from.x][data.from.y] = null;
				mailBox[data.to.x][data.to.y] = new Piece(data.piece.color, data.piece.name);
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
		public static bool InsideBoard(in int x, in int y) => -1 < x && x < 9 && -1 < y && y < 10;
	}



	public static class Extensions
	{
		/// <summary>
		/// Lấy màu ngược với màu nhập vào.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Color Opponent(this Color color) => (Color)(1 - (int)color);


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