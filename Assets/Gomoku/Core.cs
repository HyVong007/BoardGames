using System;
using System.Collections.Generic;
using UnityEngine;


namespace BoardGames.Gomoku
{
	public enum Symbol
	{
		O = 0, X = 1
	}



	public readonly struct MoveData : IMoveData<Symbol>
	{
		public Symbol playerID { get; }
		public readonly Vector2Int index;


		public MoveData(in Symbol symbol, in Vector2Int index)
		{
			playerID = symbol;
			this.index = index;
		}


		public override string ToString() => $"{{{playerID}, {index}}}, ";
	}



	public sealed class Core
	{
		#region Khai báo dữ liệu và khởi tạo
		private readonly Symbol?[][] mailBox;
		public Rect rect { get; private set; }

		/// <summary>
		/// Số ô trống trên bàn cờ.
		/// </summary>
		private int emptyCells;


		public Core(in Vector2Int size)
		{
			if (size.x < 5 || size.y < 5) throw new ArgumentOutOfRangeException($"Size phải >= (5, 5). size= {size}");
			if (size.x > 100 || size.y > 100) throw new OutOfMemoryException($"Size quá lớn. size= {size}");
			mailBox = Util.NewArray<Symbol?>(size.x, size.y);
			rect = new Rect(0, 0, size.x, size.y);
			emptyCells = size.x * size.y;
		}


		public Core(Symbol?[][] mailBox) : this(new Vector2Int(mailBox.Length, mailBox[0].Length))
		{
			// Không kiểm tra {state}, nếu {mailBox} đã kết thúc sẽ gây lỗi tiềm tàng
			for (int x = 0; x < rect.xMax; ++x)
				for (int y = 0; y < rect.yMax; ++y)
					if (mailBox[x][y] != null)
					{
						this.mailBox[x][y] = mailBox[x][y];
						--emptyCells;
					}
		}


		public Core(Core core) : this(core.mailBox)
		{
			if (core.state != null) throw new InvalidOperationException("Không thể copy Board đã kết thúc !");
		}


		public Symbol? this[int x, int y] => mailBox[x][y];
		#endregion


		#region State
		public enum State
		{
			O_Win = 0, X_Win = 1, Draw = 2
		}
		public State? state { get; private set; }
		public readonly IReadOnlyList<ReadOnlyArray<Vector3>> winLines = new List<ReadOnlyArray<Vector3>>();
		private static readonly Vector3 WINLINE_DELTA = new(0.5f, 0.5f);
		public event Action<State?> onStateChanged;
		#endregion


		#region DEBUG
		public override string ToString()
		{
			string s = "";
			for (int y = (int)rect.yMax - 1; y >= 0; --y)
			{
				s += $"{y}    ";
				for (int x = 0; x < mailBox.Length; ++x)
				{
					var symbol = mailBox[x][y];
					s += symbol != null ? $"  {symbol}  " : "  *  ";
				}
				s += "\n\n";
			}

			s += "\n     ";
			for (int x = 0; x < rect.xMax; ++x) s += $"  {x}  ";
			return s;
		}


		public void Print()
		{
			for (int y = (int)rect.yMax - 1; y >= 0; --y)
			{
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write($"{y}    ");
				for (int x = 0; x < mailBox.Length; ++x)
				{
					var symbol = mailBox[x][y];
					Console.ForegroundColor = symbol == Symbol.O ? ConsoleColor.Red : symbol == Symbol.X ? ConsoleColor.Green : ConsoleColor.DarkYellow;
					Console.Write(symbol != null ? $"  {symbol}  " : "  *  ");
				}
				Console.WriteLine("\n");
			}

			Console.ForegroundColor = ConsoleColor.White;
			Console.Write("\n     ");
			for (int x = 0; x < rect.xMax; ++x) Console.Write($"  {x}  ");
			Console.WriteLine();
		}
		#endregion


		#region Move
		/// <summary>
		/// Vector Phương = { Ngang, Dọc, Chéo Thuận, Chéo Nghịch}
		/// <para><c>AXES[i] == { Direction (Vector Chiều) }</c></para> 
		/// </summary>
		private static readonly Vector2Int[][] AXES = new Vector2Int[][]
		{
			new Vector2Int[]{new (-1, 0), new (1, 0)},	// Ngang
			new Vector2Int[]{new (0, 1), new (0, -1)},	// Dọc
			new Vector2Int[]{new (-1, -1), new (1, 1)},	// Chéo Thuận
			new Vector2Int[]{new(-1, 1), new (1, -1)},	// Chéo Nghịch
		};


		public void Move(MoveData data, MoveType mode)
		{
			if (mode != MoveType.Undo)
			{
				#region DO
				mailBox[data.index.x][data.index.y] = data.playerID;
				--emptyCells;

				#region Kiểm tra {data.playerID} có chiến thắng hay bàn cờ hòa ?
				var enemySymbol = 1 - data.playerID;
				var winLines = this.winLines as List<ReadOnlyArray<Vector3>>;
				winLines.Clear();

				for (int a = 0; a < 4; ++a)
				{
					var axe = AXES[a];
					var line = new Vector3[] { data.index.ToVector3() + WINLINE_DELTA, data.index.ToVector3() + WINLINE_DELTA };
					int count = 1, enemy = 0, lineIndex = 0;

					for (int d = 0; d < 2; ++d)
					{
						var direction = axe[d];
						for (int x = data.index.x + direction.x, y = data.index.y + direction.y; count <= 6 && rect.Contains(x, y); x += direction.x, y += direction.y)
						{
							var symbol = mailBox[x][y];
							if (symbol == data.playerID) { line[lineIndex] = new Vector3(x, y) + WINLINE_DELTA; ++count; continue; }
							if (symbol == enemySymbol) ++enemy;
							break;
						}

						if (count > 5) goto CONTINUE_LOOP_AXE;
						++lineIndex;
					}

					if (count == 5 && enemy < 2) winLines.Add(new(line));
					CONTINUE_LOOP_AXE:;
				}

				if (winLines.Count != 0)
				{
					state = (State)data.playerID;
					onStateChanged?.Invoke(state);
				}
				else if (emptyCells == 0)
				{
					state = State.Draw;
					onStateChanged?.Invoke(state);
				}
				#endregion
				#endregion
			}
			else
			{
				#region UNDO
				mailBox[data.index.x][data.index.y] = null;
				++emptyCells;

				#region Cập nhật state
				var oldState = state;
				state = null;
				if (oldState != null) onStateChanged?.Invoke(state);
				#endregion
				#endregion
			}
		}
		#endregion
	}
}