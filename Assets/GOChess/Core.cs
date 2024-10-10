﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;
using History = BoardGames.History<BoardGames.GOChess.Color, BoardGames.GOChess.MoveData>;


namespace BoardGames.GOChess
{
	public enum Color
	{
		White = 0, Black = 1
	}



	/// <summary>
	/// "Hòn đảo" hoặc "Đất liền": tập hợp liên tục các quân cờ cùng màu có tiếp xúc với nhau<br/>
	/// Trong khi đó vùng trống trên bàn cờ là biển<br/>
	/// 1 quân cờ riêng lẻ gọi là "Hòn đảo cô đơn". Nếu đảo bị bao vây hết đường bờ biển thì đảo sẽ biến mất (chết quân).
	/// </summary>
	public sealed class Land
	{
		public readonly Color color;
		/// <summary>
		/// Số "lổ thở" của land hoặc độ dài đường bờ biển: Lổ thở là ô trống ngay sát quân cờ của land và lổ thở ở các vị trí trên/dưới/trái/phải so với quân cờ<br/>
		/// Đường bờ biển càng dài thì land càng vững mạnh (khó chết)<para/>
		/// CHÚ Ý: Các lổ thở của 1 land có thể trùng nhau và các land có thể có chung các lổ thở !
		/// </summary>
		public int airHole;
		public readonly List<Vector2Int> indexes = new List<Vector2Int>();


		public Land(in Color color) => this.color = color;
		public Land(Land land) : this(land.color)
		{
			airHole = land.airHole;
			indexes.AddRange(land.indexes);
		}
		public override string ToString() => $"({color}, airHole= {airHole}, indexes.Count= {indexes.Count}), ";
	}



	public readonly struct MoveData : IMoveData<Color>
	{
		public Color playerID { get; }
		public readonly Vector2Int index;
		public readonly byte emptyHole;
		public readonly IReadOnlyDictionary<Color, IReadOnlyDictionary<Land, byte>> color_land_point;


		public MoveData(Core core, in Color color, in Vector2Int index)
		{
			playerID = color;
			this.index = index;
			var color_land_point = new Dictionary<Color, IReadOnlyDictionary<Land, byte>>
			{
				[Color.White] = new Dictionary<Land, byte>(),
				[Color.Black] = new Dictionary<Land, byte>()
			};
			this.color_land_point = color_land_point;
			emptyHole = 0;
			for (int d = 0; d < 4; ++d)
			{
				var pos = index + Core.DIRECTIONS[d];
				if (!core.rect.Contains(pos)) continue;
				var land = core[pos.x, pos.y];
				if (land == null) ++emptyHole;
				else if (color_land_point[land.color].ContainsKey(land)) ++(color_land_point[land.color] as Dictionary<Land, byte>)[land];
				else (color_land_point[land.color] as Dictionary<Land, byte>)[land] = 1;
			}
		}


		public MoveData(in Color playerID, in Vector2Int index, in byte emptyHole, IReadOnlyDictionary<Color, IReadOnlyDictionary<Land, byte>> color_land_point)
		{
			this.playerID = playerID;
			this.index = index;
			this.emptyHole = emptyHole;
			this.color_land_point = color_land_point;
		}


		public override string ToString() =>
			$"data: ({(Color)playerID}, index= {index}, emptyHole= {emptyHole}, ally count= {color_land_point[(Color)playerID].Count}, enemy count= {color_land_point[(Color)(1 - playerID)].Count}), ";
	}



	public sealed class Core
	{
		#region Khai báo dữ liệu và khởi tạo
		public static readonly ReadOnlyArray<Vector2Int> DIRECTIONS = new(new[]
		{
			Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
		});
		private readonly IReadOnlyDictionary<Color, List<Land>> lands = new Dictionary<Color, List<Land>>
		{
			[Color.White] = new List<Land>(),
			[Color.Black] = new List<Land>()
		};
		private Land[][] mailBox;
		public Rect rect { get; private set; }


		public Core(in Vector2Int size)
		{
			if (size.x < 2 || size.y < 2) throw new ArgumentOutOfRangeException($"Size phải >= (2, 2). size= {size}");
			if (size.x > 100 || size.y > 100) throw new OutOfMemoryException($"Size quá lớn. size= {size}");
			mailBox = new Land[size.x][];
			for (int x = 0; x < size.x; ++x) mailBox[x] = new Land[size.y];
			rect = new Rect(0, 0, size.x, size.y);
		}


		public Core(Color?[][] mailBox) : this(new Vector2Int(mailBox.Length, mailBox[0].Length))
		{
			var index = new Vector2Int();
			for (index.x = 0; index.x < rect.width; ++index.x)
				for (index.y = 0; index.y < rect.height; ++index.y)
					if (mailBox[index.x][index.y] != null)
						Move(new MoveData(this, mailBox[index.x][index.y].Value, index), History.Mode.Play);
		}


		public Land this[int x, int y] => mailBox[x][y];
		public Land this[Vector2Int index] => mailBox[index.x][index.y];
		#endregion


		#region State
		private readonly Dictionary<Color, short> pieceCounts = new Dictionary<Color, short>
		{
			[Color.White] = -1,
			[Color.Black] = -1
		};


		public int PieceCount(Color color)
		{
			if (pieceCounts[0] >= 0) return pieceCounts[color];

			int c = 0;
			var list = lands[color];
			for (int i = 0; i < list.Count; ++i) c += list[i].indexes.Count;
			return pieceCounts[color] = (short)c;
		}


		public enum State
		{
			White_Win, Black_Win, Draw
		}
		public State? state { get; private set; }
		public event Action<State> onFinished;


		/// <summary>
		/// Kết thúc ván chơi và quyết định kết quả.
		/// <para>Chú ý: Không thể Undo Finish !</para>
		/// </summary>
		public State Finish()
		{
			int w = PieceCount(Color.White), b = PieceCount(Color.Black);
			state = w > b ? State.White_Win : b > w ? State.Black_Win : State.Draw;
			onFinished?.Invoke(state.Value);
			return state.Value;
		}
		#endregion


		#region DEBUG
		public override string ToString()
		{
			string s = "";
			for (int y = (int)rect.yMax - 1; y >= 0; --y)
			{
				s += $"{y}    ";
				for (int x = 0; x < mailBox.Length; ++x)
					s += mailBox[x][y] == null ? "  *  " : mailBox[x][y].color == Color.White ? "  W  " : "  B  ";
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
					Console.ForegroundColor = mailBox[x][y] == null ? ConsoleColor.DarkYellow : mailBox[x][y].color == Color.White ? ConsoleColor.Red : ConsoleColor.Green;
					Console.Write(mailBox[x][y] == null ? "  *  " : mailBox[x][y].color == Color.White ? "  O  " : "  X  ");
				}
				Console.WriteLine("\n");
			}

			Console.ForegroundColor = ConsoleColor.White;
			Console.Write("\n     ");
			for (int x = 0; x < rect.xMax; ++x) Console.Write($"  {x}  ");
			Console.WriteLine();
		}
		#endregion


		#region CanMove
		/// <summary>
		/// Temporary cache<br/>
		/// point là số tiếp điểm của land với ô đang kiểm tra<br/>
		/// </summary>
		private readonly IReadOnlyDictionary<Color, Dictionary<Land, byte>> color_land_point = new Dictionary<Color, Dictionary<Land, byte>>
		{
			[Color.White] = new Dictionary<Land, byte>(),
			[Color.Black] = new Dictionary<Land, byte>()
		};

		public bool CanMove(in Color color, in Vector2Int index)
		{
			if (mailBox[index.x][index.y] != null) return false;

			color_land_point[Color.White].Clear();
			color_land_point[Color.Black].Clear();
			for (int d = 0; d < 4; ++d)
			{
				var pos = index + DIRECTIONS[d];
				if (!rect.Contains(pos)) continue;
				var land = mailBox[pos.x][pos.y];
				if (land == null) return true;

				if (color_land_point[land.color].ContainsKey(land)) ++color_land_point[land.color][land];
				else color_land_point[land.color][land] = 1;
			}

			foreach (var ally_point in color_land_point[color])
				if (ally_point.Key.airHole > ally_point.Value) return true;

			foreach (var enemy_point in color_land_point[color.Opponent()])
				if (enemy_point.Key.airHole == enemy_point.Value) return true;

			return false;
		}
		#endregion


		#region Move
		public event Action<Vector3Int, Color> drawPieceGUI;
		public event Action<Vector3Int> clearPieceGUI;

		/// <summary>
		/// point là số tiếp điểm của land với ô đang kiểm tra<br/>
		/// </summary>
		private readonly Dictionary<Land, byte> land_point = new();
		public void Move(in MoveData data, History.Mode mode)
		{
			pieceCounts[Color.White] = pieceCounts[Color.Black] = -1;
			land_point.Clear();
			if (mode != History.Mode.Undo)
			{
				#region DO
				#region Tạo land mới vô bàn cờ và copy tất cả ally hiện tại, lấy ally hiện tại khỏi bàn cờ
				var newLand = new Land((Color)data.playerID);
				newLand.indexes.Add(data.index);
				newLand.airHole = data.emptyHole;
				lands[newLand.color].Add(newLand);
				drawPieceGUI?.Invoke(data.index.ToVector3Int(), (Color)data.playerID);

				foreach (var ally_point in data.color_land_point[newLand.color])
				{
					newLand.airHole += ally_point.Key.airHole - ally_point.Value;
					newLand.indexes.AddRange(ally_point.Key.indexes);
					lands[ally_point.Key.color].Remove(ally_point.Key);
				}

				for (int i = 0; i < newLand.indexes.Count; ++i)
				{
					var index = newLand.indexes[i];
					mailBox[index.x][index.y] = newLand;
				}
				#endregion

				#region Trừ lổ thở của enemy land
				foreach (var enemy_point in data.color_land_point[newLand.color.Opponent()])
				{
					if ((enemy_point.Key.airHole -= enemy_point.Value) > 0) continue;

					// enemy bị chết, lấy khỏi bàn cờ
					// xóa các quân cờ của enemy và tăng lổ thở cho các land xung quanh (nếu có, != enemy)
					var enemy = enemy_point.Key;
					lands[enemy.color].Remove(enemy);
					for (int i = enemy.indexes.Count - 1; i >= 0; --i)
					{
						var index = enemy.indexes[i];
						mailBox[index.x][index.y] = null;
						clearPieceGUI?.Invoke(index.ToVector3Int());
						for (int d = 0; d < 4; ++d)
						{
							var surround = index + DIRECTIONS[d];
							if (!rect.Contains(surround)) continue;
							var land = mailBox[surround.x][surround.y];
							if (land == null || land == enemy) continue;
							if (land_point.ContainsKey(land)) ++land_point[land];
							else land_point[land] = 1;
						}

						foreach (var kvp in land_point) kvp.Key.airHole += kvp.Value;
					}
				}
				#endregion
				#endregion
			}
			else
			{
				#region UNDO
				mailBox[data.index.x][data.index.y] = null;
				clearPieceGUI?.Invoke(data.index.ToVector3Int());

				#region Khôi phục ally land vào bàn cờ
				foreach (var ally in data.color_land_point[(Color)data.playerID].Keys)
				{
					lands[ally.color].Add(ally);
					for (int i = ally.indexes.Count - 1; i >= 0; --i)
					{
						var index = ally.indexes[i];
						mailBox[index.x][index.y] = ally;
					}
				}
				#endregion

				#region Khôi phục lổ thở của  enemy land
				foreach (var enemy_point in data.color_land_point[(Color)(1 - data.playerID)])
				{
					if ((enemy_point.Key.airHole += enemy_point.Value) > enemy_point.Value) continue;

					// enemy đã chết trước đó => hồi sinh vào bàn cờ và vẽ các quân cờ
					// Trừ lổ thở của các land xung quanh (nếu có, !=enemy)
					var enemy = enemy_point.Key;
					lands[enemy.color].Add(enemy);
					for (int i = 0; i < enemy.indexes.Count; ++i)
					{
						var index = enemy.indexes[i];
						mailBox[index.x][index.y] = enemy;
						drawPieceGUI?.Invoke(index.ToVector3Int(), enemy.color);
						for (int d = 0; d < 4; ++d)
						{
							var surround = index + DIRECTIONS[d];
							if (!rect.Contains(surround)) continue;
							var land = mailBox[surround.x][surround.y];
							if (land == null || land == enemy) continue;
							if (land_point.ContainsKey(land)) ++land_point[land];
							else land_point[land] = 1;
						}

						foreach (var kvp in land_point) kvp.Key.airHole -= kvp.Value;
					}
				}
				#endregion
				#endregion
			}
		}
		#endregion
	}



	public static class Extensions
	{
		/// <summary>
		/// Lấy màu ngược với màu nhập vào.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Color Opponent(this Color color) => (Color)(1 - (int)color);
	}
}