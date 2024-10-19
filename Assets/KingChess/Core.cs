using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using History = BoardGames.History<BoardGames.KingChess.Color, BoardGames.KingChess.MoveData>;


namespace BoardGames.KingChess
{
	public enum Color
	{
		White = 0, Black = 1
	}



	public enum PieceName
	{
		Pawn = 0, Rook = 1, Bishop = 2, Knight = 3, Queen = 4, King = 5
	}



	public readonly struct MoveData : IMoveData<Color>
	{
		public Color playerID { get; }
		public readonly int from, to;
		public readonly PieceName name;
		public readonly PieceName? capturedName;

		/// <summary>
		/// Lịch sử của quân Xe bị bắt.
		/// </summary>
		public readonly (bool moved, int count) capturedRookHistory;

		/// <summary>
		/// Bắt Tốt qua đường (Enpassant): index của quân đối phương bị bắt thông qua enpassant.
		/// </summary>
		public readonly int? enpassantCapturedIndex;

		/// <summary>
		/// Quân sẽ được phong cấp (promotion).<br/>
		/// Không thể phong cấp thành <see cref="PieceName.Pawn"/> hoặc <see cref="PieceName.King"/>
		/// <para>!= <see langword="null"/> khi: <c><see cref="name"/> == <see cref="PieceName.Pawn"/></c> và tọa độ <see cref="to"/> nằm ở rank cuối cùng (<see cref="RANK_1"/> hoặc <see cref="RANK_8"/> tùy theo <see cref="MoveData.playerID"/>)</para>
		/// </summary>
		public readonly PieceName? promotedName;

		public enum Castling
		{
			None = 0, Near = 1, Far = 2
		}
		/// <summary>
		/// Trạng thái nhập Thành<para/>
		/// Luôn di chuyển Vua đầu tiên và Vua di chuyển gây ra Nhập Thành
		/// </summary>
		public readonly Castling castling;


		public override string ToString() => $"color= {playerID}, name= {name}, from= {from.ToMailBoxIndex()}, to= {to.ToMailBoxIndex()}, capturedName= {capturedName}, " +
				$"enpassantCapturedIndex= {enpassantCapturedIndex?.ToMailBoxIndex()}, promotedName= {promotedName}, " +
				$"capturedRookHistory= {capturedRookHistory}, castling= {castling}";


		private MoveData(Core core, in Color playerID, in PieceName name, in int from, in int to, PieceName? promotedName)
		{
			var m_to = to.ToMailBoxIndex();
			this.playerID = playerID;
			this.name = name;
			this.from = from;
			this.to = to;
			capturedName = core[m_to.x, m_to.y]?.name;
			enpassantCapturedIndex = null;
			castling = Castling.None;
			this.promotedName = null;

			if (capturedName == PieceName.Rook)
				capturedRookHistory = core.rookHistory[1 - playerID][to];
			else capturedRookHistory = (false, -1);

			if (name == PieceName.Pawn)
			{
				#region Kiểm tra Phong cấp (Pawn Promotion)
				if ((playerID == Color.White && to >= 56)
					|| (playerID == Color.Black && to <= 7))
				{
					this.promotedName = promotedName;
					return;
				}
				#endregion

				#region Kiểm tra Bắt Tốt qua đường (Enpassant)
				if (capturedName == null)
					enpassantCapturedIndex = playerID == Color.White ?
					(from + 7 == to ? from - 1 : from + 9 == to ? from + 1 : null)
					: (from - 7 == to ? from + 1 : from - 9 == to ? from - 1 : null);

				if (enpassantCapturedIndex != null) capturedName = PieceName.Pawn;
				#endregion
			}
			else if (name == PieceName.King && core.kingHistory[playerID] == (false, 0))
				castling = playerID == Color.White ?
					(to == 6 ? Castling.Near : to == 2 ? Castling.Far : Castling.None)
					: (to == 62 ? Castling.Near : to == 58 ? Castling.Far : Castling.None);
		}


		public static Func<Color, CancellationToken, UniTask<PieceName?>> getPromotedName;
		public static async UniTask<MoveData> New(Core core, Vector2Int from, Vector2Int to, CancellationToken token = default)
		{
			var (playerID, name) = core[from.x, from.y].Value;
			PieceName? promotedName = null;

			#region Kiểm tra Phong cấp (Pawn Promotion)
			if (name == PieceName.Pawn &&
				((playerID == Color.White && to.ToBitIndex() >= 56)
				|| (playerID == Color.Black && to.ToBitIndex() <= 7)))
				promotedName = await getPromotedName(playerID, token);
			#endregion

			return new(core, playerID, name, from.ToBitIndex(), to.ToBitIndex(), promotedName);
		}
	}



	public sealed class Core
	{
		private const ulong
			CENTER = 0x1818000000UL,
			EXTENDED_CENTER = 0x3C3C3C3C0000UL,
			FILE_A = 0x101010101010101UL,
			FILE_H = 0x8080808080808080UL,
			FILE_AB = 0x303030303030303UL,
			FILE_GH = 0xC0C0C0C0C0C0C0C0UL,
			RANK_1 = 0xFFUL,
			RANK_4 = 0xFF000000UL,
			RANK_5 = 0xFF00000000UL,
			RANK_8 = 0xFF00000000000000UL;


		#region Khai báo dữ liệu và khởi tạo
		/// <summary>
		/// Nhớ update <see cref="mailBox"/> sau khi kết thúc modify
		/// </summary>
		private readonly IReadOnlyDictionary<Color, Dictionary<PieceName, ulong>> bitboards = new Dictionary<Color, Dictionary<PieceName, ulong>>
		{
			[Color.White] = new Dictionary<PieceName, ulong>
			{
				[PieceName.Bishop] = 0UL,
				[PieceName.King] = 0UL,
				[PieceName.Knight] = 0UL,
				[PieceName.Pawn] = 0UL,
				[PieceName.Queen] = 0UL,
				[PieceName.Rook] = 0UL
			},
			[Color.Black] = new Dictionary<PieceName, ulong>
			{
				[PieceName.Bishop] = 0UL,
				[PieceName.King] = 0UL,
				[PieceName.Knight] = 0UL,
				[PieceName.Pawn] = 0UL,
				[PieceName.Queen] = 0UL,
				[PieceName.Rook] = 0UL
			}
		};
		private readonly (Color playerID, PieceName name)?[][] mailBox;

		private static readonly (Color playerID, PieceName name)?[][] DEFAULT_MAILBOX = new (Color playerID, PieceName name)?[][]
		{
			// A
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Rook), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Rook)},

			// B
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Knight), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Knight)},

			// C
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Bishop), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Bishop)},
			
			// D
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Queen), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Queen)},

			// E
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.King), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.King)},

			// F
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Bishop), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Bishop)},

			// G
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Knight), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Knight)},

			// H
			new (Color playerID, PieceName name)?[]{(Color.White, PieceName.Rook), (Color.White, PieceName.Pawn), null, null, null, null, (Color.Black, PieceName.Pawn), (Color.Black, PieceName.Rook)},
		};

		public Core(History history = null, (Color playerID, PieceName name)?[][] mailBox = null)
		{
			this.history = history ?? new History();
			if (mailBox != null && (mailBox.Length != 8 || mailBox[0].Length != 8))
				throw new ArgumentOutOfRangeException("mailBox phải là 8x8 !");
			mailBox ??= DEFAULT_MAILBOX;

			#region Khởi tạo {bitboards}, {mailBox}, {rookHistory}
			rookHistory = new Dictionary<Color, Dictionary<int, (bool moved, int count)>>
			{
				[Color.White] = new Dictionary<int, (bool moved, int count)>(),
				[Color.Black] = new Dictionary<int, (bool moved, int count)>()
			};

			int index = 0;
			this.mailBox = Util.NewArray(8, 8, (x, y) =>
			{
				if (mailBox[x][y] != null)
				{
					var (playerID, name) = mailBox[x][y].Value;
					bitboards[playerID][name] = bitboards[playerID][name].SetBit(index);

					if (name == PieceName.Rook)
						rookHistory[playerID][index] =
						(playerID == Color.White && (index == 0 || index == 7)) ? (false, 0)
						: (playerID == Color.Black && (index == 56 || index == 63)) ? (false, 0)
						: (true, 0);

					++index;
					return mailBox[x][y];
				}

				++index;
				return null;
			});
			#endregion

			kingHistory = new Dictionary<Color, (bool moved, int count)>
			{
				[Color.White] = mailBox[4][0] == (Color.White, PieceName.King) ? (false, 0) : (true, 0),
				[Color.Black] = mailBox[4][7] == (Color.Black, PieceName.King) ? (false, 0) : (true, 0)
			};

			states = new Dictionary<Color, State>
			{
				[Color.White] = State.Normal,
				[Color.Black] = State.Normal
			};
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static (Color playerID, PieceName name)?[][] CloneDefaultMailBox()
		=> Util.NewArray(8, 8, (x, y) => DEFAULT_MAILBOX[x][y]);
		#endregion


		#region Lịch sử quân cờ dùng để kiểm tra Nhập thành
		/// <summary>
		/// moved == <see langword="true"/>: đã di chuyển, không thể khôi phục trạng thái ban đầu.
		/// </summary>
		public readonly IReadOnlyDictionary<Color, (bool moved, int count)> kingHistory;

		/// <summary>
		/// moved == <see langword="true"/>: đã di chuyển, không thể khôi phục trạng thái ban đầu.
		/// </summary>
		public readonly IReadOnlyDictionary<Color, Dictionary<int, (bool moved, int count)>> rookHistory;
		#endregion


		#region State: trạng thái bàn cờ: Vua có bị chiếu hay chiếu bí
		public enum State
		{
			Normal, Check, CheckMate
		}

		private Dictionary<Color, State> states;
		public event Action<Color, State> onStateChanged;


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public State GetState(Color playerID) => states[playerID];


		///<summary>
		/// Vua <paramref name="playerID"/> có bị chiếu ?
		///</summary>
		/// <param name="playerID">Màu của quân Vua đang kiểm tra.</param>
		private bool KingIsChecked(Color playerID, in MoveData lastMoveData)
		{
			ulong king = bitboards[playerID][PieceName.King];
			var enemyColor = 1 - playerID;
			foreach (var name in ALL_PIECE_NAMES.Random())
				if ((king & FindPseudoLegalMoves(enemyColor, name, lastMoveData)) != 0) return true;

			return false;
		}
		#endregion


		#region DEBUG
		public override string ToString()
		{
			string s = "";
			for (int y = mailBox[0].Length - 1; y >= 0; --y)
			{
				s += $" {y + 1}  ";
				for (int x = 0; x < mailBox.Length; ++x)
				{
					var d = mailBox[x][y];
					s += d != null ? $"  {(d.Value.playerID == Color.White ? PIECENAME_STRING[d.Value.name] : PIECENAME_STRING[d.Value.name].ToLower())}  " : "  *  ";
				}
				s += "\n";
			}
			s += "\n      A    B    C    D    E    F    G    H ";
			return s;
		}


		public void Print()
		{
			for (int y = mailBox[0].Length - 1; y >= 0; --y)
			{
				Console.ForegroundColor = ConsoleColor.DarkRed;
				Console.Write($"{y + 1}  ");
				for (int x = 0; x < mailBox.Length; ++x)
				{
					var d = mailBox[x][y];
					if (d != null)
					{
						Console.ForegroundColor = d.Value.playerID == Color.White ? ConsoleColor.White : ConsoleColor.DarkCyan;
						Console.Write($"  {PIECENAME_STRING[d.Value.name]}  ");
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.DarkYellow;
						Console.Write("  *  ");
					}
				}
				Console.WriteLine("\n");
			}

			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("\n     A    B    C    D    E    F    G    H ");
			Console.ForegroundColor = ConsoleColor.White;
		}


		private static readonly IReadOnlyDictionary<PieceName, string> PIECENAME_STRING = new Dictionary<PieceName, string>
		{
			[PieceName.Pawn] = "P",
			[PieceName.Rook] = "R",
			[PieceName.Knight] = "N",
			[PieceName.Bishop] = "B",
			[PieceName.Queen] = "Q",
			[PieceName.King] = "K"
		};
		#endregion


		#region Properties và Indexers: truy vấn (query) các Bitboards
		/// <summary>
		/// Bitboard tập hợp những quân cờ cùng màu.<br/>
		/// Bit 1 = có quân cờ
		/// </summary>
		public ulong this[Color playerID]
		{
			get
			{
				ulong result = 0UL;
				for (int pn = 0; pn < 6; ++pn) result |= bitboards[playerID][(PieceName)pn];
				return result;
			}
		}


		/// <summary>
		/// Bitboard tập hợp các quân cờ cùng tên và màu.<br/>
		/// Bit 1 = có quân cờ
		/// </summary>
		public ulong this[Color playerID, PieceName name] => bitboards[playerID][name];


		/// <summary>
		/// Ô tại vị trí (x, y) trên bàn cờ.
		/// </summary>
		public (Color color, PieceName name)? this[int x, int y] => mailBox[x][y];


		/// <summary>
		/// Bitboard tập hợp các quân cờ (bất kỳ).<br/>
		/// Bit 1 = có quân cờ
		/// </summary>
		public ulong occupied => this[Color.White] | this[Color.Black];


		/// <summary>
		/// Bitboard tập hợp các ô trống.<br/>
		/// Bit 1 = ô trống
		/// </summary>
		public ulong empty => ~this[Color.White] & ~this[Color.Black];
		#endregion


		#region FindSlicingMoves
		private enum Direction
		{
			Left = 0, Right = 1, Up = 2, Down = 3,
			LeftDown = 4, RightUp = 5, LeftUp = 6, RightDown = 7
		}
		private const bool LEFT_SHIFT = true, RIGHT_SHIFT = false;

		/// <summary>
		/// Constant Data cho di chuyển quân cờ theo <see cref="Direction"/>
		/// <para><c>[<see cref="Direction"/>]=data</c>:</para>
		/// <para>NOT_BORDER: Tập hợp các ô không phải biên của <see cref="Direction"/> trên bàn cờ. Nếu quân cờ nằm trên biên của <see cref="Direction"/> thì không thể di chuyển.</para>
		/// <para>SHIFT: Nên dùng toán tử &lt;&lt; hay &gt;&gt; ? </para>
		/// <para>STEP: Số bước shift <c>(&lt;&lt;STEP hoặc &gt;&gt;STEP)</c></para>
		/// </summary>
		private static readonly IReadOnlyDictionary<Direction, (ulong NOT_BORDER, bool SHIFT, int STEP)> EIGHT_DIRECTIONS_DATA = new Dictionary<Direction, (ulong NOT_BORDER, bool SHIFT, int STEP)>
		{
			[Direction.Left] = (~FILE_A, RIGHT_SHIFT, 1),
			[Direction.Right] = (~FILE_H, LEFT_SHIFT, 1),
			[Direction.Up] = (~RANK_8, LEFT_SHIFT, 8),
			[Direction.Down] = (~RANK_1, RIGHT_SHIFT, 8),
			[Direction.LeftDown] = (~FILE_A & ~RANK_1, RIGHT_SHIFT, 9),
			[Direction.RightUp] = (~FILE_H & ~RANK_8, LEFT_SHIFT, 9),
			[Direction.LeftUp] = (~FILE_A & ~RANK_8, LEFT_SHIFT, 7),
			[Direction.RightDown] = (~FILE_H & ~RANK_1, RIGHT_SHIFT, 7)
		};


		/// <summary>
		/// Tìm các ô có thể di chuyển của quân cờ theo quy tắc:<br/>
		///- Có thể đi qua ô trống<br/>
		///- Bị chặn bởi ô chứa quân bất kỳ<br/>
		///- Nếu ô chứa quân khác màu thì ô là 1 move (ăn quân)
		///<para> (bit 1 = move-square)</para>
		/// </summary>
		/// <param name="source">Tập hợp quân cờ cần tìm moves</param>
		/// <param name="EMPTY">Tập hợp ô trống</param>
		/// <param name="EMPTY_OR_OPPONENT">Tập hợp ô trống hoặc chứa quân cờ đối phương (khác màu với quân cờ trong source)</param>
		///<param name="MAX_STEP">Số nước đi liên tiếp tối đa (MAX_STEP &gt; 0)</param>
		private static ulong FindSlicingMoves(ulong source, in Direction direction, in ulong EMPTY, in ulong EMPTY_OR_OPPONENT, in byte MAX_STEP = byte.MaxValue)
		{
			ulong result = 0UL;
			var (NOT_BORDER, SHIFT, STEP) = EIGHT_DIRECTIONS_DATA[direction];
			byte step = 0;
			while (true)
			{
				source &= NOT_BORDER;
				if (source == 0) break;

				source = SHIFT == LEFT_SHIFT ? source << STEP : source >> STEP;
				result |= source & EMPTY_OR_OPPONENT;
				if (++step == MAX_STEP) break;

				source &= EMPTY;
			}

			return result;
		}
		#endregion


		/// <summary>
		/// Tìm tập hợp các ô có thể di chuyển của quân cờ theo luật của từng loại quân cờ. Chưa kiểm tra xem King color có bị chiếu.
		/// <para>(bit 1 = move-square)</para>
		/// </summary>
		/// <param name="index"><c>if index != <see langword="null"/>:</c> Chỉ trả về tập hợp moves của quân cờ tại index.</param>
		private ulong FindPseudoLegalMoves(Color playerID, PieceName name, in MoveData? lastMoveData, int? index = null)
		{
			ulong SOURCE = index != null ? bitboards[playerID][name].GetBit(index.Value) : bitboards[playerID][name];
			if (SOURCE == 0) return 0UL;

			ulong result = 0UL;
			ulong EMPTY = empty;
			ulong ENEMY = this[1 - playerID];
			ulong EMPTY_OR_ENEMY = EMPTY | ENEMY;

			switch (name)
			{
				case PieceName.Pawn:
					#region Pawn moves
					if (playerID == Color.White)
					{
						#region White Pawn Moves: dịch chuyển bằng Left Shift <<
						ulong P_ls = SOURCE << 7;
						result |= P_ls & ENEMY & ~FILE_H;     // ăn chéo trái
						P_ls <<= 1;
						ulong forward1 = P_ls & EMPTY;              // đi về trước 1 bước
						result |= forward1;
						result |= forward1 << 8 & EMPTY & RANK_4;   // đi về trước 2 bước
						P_ls <<= 1;
						result |= P_ls & ENEMY & ~FILE_A;     // ăn chéo phải		
						#endregion
					}
					else
					{
						#region Black Pawn Moves: dịch chuyển bằng Right Shift >>
						ulong P_rs = SOURCE >> 7;
						result |= P_rs & ENEMY & ~FILE_A;     // ăn chéo trái
						P_rs >>= 1;
						ulong forward1 = P_rs & EMPTY;              // đi về trước 1 bước
						result |= forward1;
						result |= forward1 >> 8 & EMPTY & RANK_5;   // đi về trước 2 bước
						P_rs >>= 1;
						result |= P_rs & ENEMY & ~FILE_H;     // ăn chéo phải
						#endregion
					}

					#region Enpassant
					// Chỉ lấy tốt của mình ở RANK_5 hoặc RANK_4
					ulong P = SOURCE & (playerID == Color.White ? RANK_5 : RANK_4);
					if (P == 0) return result;

					// Kiểm tra xem có tốt địch nào của lượt mới nhất, nằm bên trái hoặc phải của tốt mình
					if (lastMoveData == null) return result;

					var data = lastMoveData.Value;
					if (data.playerID != playerID && data.name == PieceName.Pawn
						&& Mathf.Abs(data.from - data.to) == 16)
					{
						ulong opponentP = bitboards[data.playerID][PieceName.Pawn].GetBit(data.to);
						if (((P << 1) & opponentP) != 0 || ((P >> 1) & opponentP) != 0)
							result = result.SetBit(data.playerID == (int)Color.White ? data.from + 8 : data.to + 8);
					}
					#endregion

					return result;
				#endregion

				case PieceName.Rook:
					#region Rook moves
					for (int i = 0; i < 4; ++i) result |= FindSlicingMoves(SOURCE, (Direction)i, EMPTY, EMPTY_OR_ENEMY);
					return result;
				#endregion

				case PieceName.Bishop:
					#region Bishop moves
					for (int i = 4; i < 8; ++i) result |= FindSlicingMoves(SOURCE, (Direction)i, EMPTY, EMPTY_OR_ENEMY);
					return result;
				#endregion

				case PieceName.Queen:
					#region Queen moves
					for (int i = 0; i < 8; ++i) result |= FindSlicingMoves(SOURCE, (Direction)i, EMPTY, EMPTY_OR_ENEMY);
					return result;
				#endregion

				case PieceName.Knight:
					#region Knight moves
					// U targets ( Left shift << )
					ulong K = SOURCE;
					result |= (K <<= 6) & ~FILE_GH; // L-L-U
					result |= (K <<= 4) & ~FILE_AB; // R-R-U
					result |= (K <<= 5) & ~FILE_H;  // U-U-L
					result |= (K <<= 2) & ~FILE_A;  // U-U-R

					// D targets ( Right shift >> ) 
					K = SOURCE;
					result |= (K >>= 6) & ~FILE_AB; // R-R-D
					result |= (K >>= 4) & ~FILE_GH; // L-L-D
					result |= (K >>= 5) & ~FILE_A;  // D-D-R
					result |= (K >>= 2) & ~FILE_H;  // D-D-L

					return result & EMPTY_OR_ENEMY;
				#endregion

				case PieceName.King:
					#region King moves
					for (int i = 0; i < 8; ++i) result |= FindSlicingMoves(SOURCE, (Direction)i, EMPTY, EMPTY_OR_ENEMY, 1);

					// Castling
					if (kingHistory[playerID] != (false, 0)) return result;

					ulong R = bitboards[playerID][PieceName.Rook];
					var RH = rookHistory[playerID];
					if (playerID == Color.White)
					{
						#region White King
						// Near Castling
						if (EMPTY.IsBit1(5) && EMPTY.IsBit1(6) && R.IsBit1(7) && RH[7] == (false, 0)) result = result.SetBit(6);
						// Far Castling
						if (EMPTY.IsBit1(3) && EMPTY.IsBit1(2) && EMPTY.IsBit1(1) && R.IsBit1(0) && RH[0] == (false, 0)) result = result.SetBit(2);
						#endregion
					}
					else
					{
						#region Black King
						// Near Castling
						if (EMPTY.IsBit1(61) && EMPTY.IsBit1(62) && R.IsBit1(63) && RH[63] == (false, 0)) result = result.SetBit(62);
						// Far Castling
						if (EMPTY.IsBit1(59) && EMPTY.IsBit1(58) && EMPTY.IsBit1(57) && R.IsBit1(56) && RH[56] == (false, 0)) result = result.SetBit(58);
						#endregion
					}

					return result;
				#endregion

				default: throw new ArgumentOutOfRangeException();
			}
		}


		#region FindLegalMoves: Tìm các nước đi hợp lệ sau khi đã kiểm tra King xem có bị chiếu.
		public readonly History history;

		/// <summary>
		/// Tìm các ô có thể move tới của các quân cờ (playerID, piece).
		/// <para>Đã kiểm tra an toàn: Vua vẫn an toàn ngay sau khi move.</para>
		/// <para>Return: tọa độ Bit Index các ô đi được và an toàn</para>
		/// </summary>
		/// <param name="index"><c>if index != null:</c> Chỉ tìm các moves của quân cờ tại index.</param>
		/// <returns></returns>
		private IEnumerable<int> FindLegalMoves(Color color, PieceName name, int? index)
		{
			var lastMoveData = history.moveCount != 0 ? history[history.moveCount - 1] : (MoveData?)null;

			#region {index} != null : Chỉ tìm move của 1 quân cờ tại index
			if (index != null)
			{
				ulong moves = FindPseudoLegalMoves(color, name, lastMoveData, index);
				if (moves == 0) yield break;

				int[] to = moves.Bit1_To_Index();
				for (int i = 0, from = index.Value; i < to.Length; ++i)
					if (LegalMove(from, to[i])) yield return to[i];

				yield break;
			}
			#endregion

			#region {index} == null: Tìm move của tất cả quân cờ ({playerID}, {name}) 
			ulong SOURCE = bitboards[color][name];
			if (SOURCE == 0) yield break;

			int[] froms = SOURCE.Bit1_To_Index();
			for (int f = 0; f < froms.Length; ++f)
			{
				ulong moves = FindPseudoLegalMoves(color, name, lastMoveData, froms[f]);
				if (moves == 0) continue;

				int[] to = moves.Bit1_To_Index();
				for (int t = 0, from = froms[f]; t < to.Length; ++t)
					if (LegalMove(from, to[t])) yield return to[t];
			}
			#endregion

			bool LegalMove(int from, int to)
			{
				var func = MoveData.getPromotedName;
				MoveData.getPromotedName = async (color, token) => PieceName.Queen;
				MoveData data = default;
				A();
				MoveData.getPromotedName = func;

				PseudoMove(data, undo: false);
				bool isChecked = KingIsChecked(color, data);
				PseudoMove(data, undo: true);
				return !isChecked;

				async void A() => data = await MoveData.New(this, from.ToMailBoxIndex(), to.ToMailBoxIndex());
			}
		}


		/// <summary>
		/// Tìm các ô có thể move tới của quân cờ tại index.
		/// <para>Đã kiểm tra an toàn: Vua vẫn an toàn ngay sau khi move.</para>
		/// </summary>
		public IEnumerable<int> FindLegalMoves(int index)
		{
			var move = index.ToMailBoxIndex();
			var (color, name) = mailBox[move.x][move.y].Value;
			return FindLegalMoves(color, name, index);
		}


		/// <summary>
		/// Tìm các ô có thể move tới của quân cờ tại (x, y).
		/// <para>Đã kiểm tra an toàn: Vua vẫn an toàn ngay sau khi move.</para>
		/// </summary>
		public IEnumerable<Vector2Int> FindLegalMoves(Vector2Int index)
		{
			var (color, name) = mailBox[index.x][index.y].Value;
			foreach (var move in FindLegalMoves(color, name, index.ToBitIndex()))
				yield return move.ToMailBoxIndex();
		}
		#endregion


		#region Move
		/// <summary>
		/// Constant data: Khi nhập thành Rook di chuyển từ "from" tới "to"
		/// </summary>
		internal static readonly IReadOnlyDictionary<Color, IReadOnlyDictionary<MoveData.Castling, (int from, int to, Vector2Int m_from, Vector2Int m_to)>>
			CASTLING_ROOK_MOVEMENTS = new Dictionary<Color, IReadOnlyDictionary<MoveData.Castling, (int from, int to, Vector2Int m_from, Vector2Int m_to)>>
			{
				[Color.White] = new Dictionary<MoveData.Castling, (int from, int to, Vector2Int m_from, Vector2Int m_to)>
				{
					[MoveData.Castling.Near] = (from: 7, to: 5, m_from: 7.ToMailBoxIndex(), m_to: 5.ToMailBoxIndex()),
					[MoveData.Castling.Far] = (from: 0, to: 3, m_from: 0.ToMailBoxIndex(), m_to: 3.ToMailBoxIndex())
				},
				[Color.Black] = new Dictionary<MoveData.Castling, (int from, int to, Vector2Int m_from, Vector2Int m_to)>
				{
					[MoveData.Castling.Near] = (from: 63, to: 61, m_from: 63.ToMailBoxIndex(), m_to: 61.ToMailBoxIndex()),
					[MoveData.Castling.Far] = (from: 56, to: 59, m_from: 56.ToMailBoxIndex(), m_to: 59.ToMailBoxIndex())
				},
			};

		private static readonly PieceName[] ALL_PIECE_NAMES = Enum.GetValues(typeof(PieceName)) as PieceName[];

		public void Move(in MoveData data, MoveType mode)
		{
			bool undo = mode == MoveType.Undo;
			PseudoMove(data, undo);

			#region Cập nhật {kingHistory}
			var KH = kingHistory[data.playerID];
			if (!KH.moved && data.name == PieceName.King)
			{
				if (!undo)
				{
					if (++KH.count > ushort.MaxValue) KH.moved = true;
				}
				else --KH.count;
				(kingHistory as Dictionary<Color, (bool moved, int count)>)[data.playerID] = KH;
			}
			#endregion

			var enemyColor = 1 - data.playerID;

			#region Cập nhật {rookHistory}
			var myRH = rookHistory[data.playerID];
			var enemyRH = rookHistory[enemyColor];

			if (!undo)
			{
				#region DO
				//if (data.capturedName == PieceName.Rook) enemyRH.Remove(data.to);

				if (data.name == PieceName.Rook)
				{
					var value = myRH[data.from];
					//myRH.Remove(data.from);
					if (!value.moved) if (++value.count > ushort.MaxValue) value.moved = true;
					myRH[data.to] = value;
				}
				else if (data.promotedName == PieceName.Rook) myRH[data.to] = (true, 0);
				else if (data.castling != MoveData.Castling.None)
				{
					var (from, to, _, _) = CASTLING_ROOK_MOVEMENTS[data.playerID][data.castling];
					//myRH.Remove(from);
					myRH[to] = (false, 1);
				}
				#endregion
			}
			else
			{
				#region UNDO
				if (data.name == PieceName.Rook)
				{
					var value = myRH[data.to];
					//myRH.Remove(data.to);
					if (!value.moved) --value.count;
					myRH[data.from] = value;
				}
				//else if (data.promotedName == PieceName.Rook) myRH.Remove(data.to);
				else if (data.castling != MoveData.Castling.None)
				{
					var (from, to, _, _) = CASTLING_ROOK_MOVEMENTS[data.playerID][data.castling];
					//myRH.Remove(to);
					myRH[from] = (false, 0);
				}

				if (data.capturedName == PieceName.Rook) enemyRH[data.to] = data.capturedRookHistory;
				#endregion
			}
			#endregion

			#region Cập nhật State
			var lastMoveData = history.moveCount != 0 ? history[history.moveCount - 1] as MoveData? : null;

			if (!undo)
			{
				#region DO
				var oldState = states[data.playerID];
				states[data.playerID] = State.Normal;
				if (oldState == State.Check) onStateChanged?.Invoke(data.playerID, State.Normal);

				foreach (var name in ALL_PIECE_NAMES.Random())
					if (FindLegalMoves(enemyColor, name, null).Any())
					{
						if (KingIsChecked(enemyColor, lastMoveData.Value))
						{
							states[enemyColor] = State.Check;
							onStateChanged?.Invoke(enemyColor, State.Check);
						}

						goto FINISH_UPDATING_STATE;
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

				if (KingIsChecked(data.playerID, lastMoveData.Value))
				{
					states[data.playerID] = State.Check;
					onStateChanged?.Invoke(data.playerID, State.Check);
				}
				#endregion
			}

		FINISH_UPDATING_STATE:;
			#endregion
		}


		/// <summary>
		/// Đi 1 nước/ Hủy 1 nước. Chỉ tác động đến <see cref="bitboards"/> và <see cref="mailBox"/>
		/// <para>Có thể dùng để test và hủy nước đi sau khi test.</para>
		/// </summary>
		private void PseudoMove(in MoveData data, bool undo)
		{
			ulong PIECE = bitboards[data.playerID][data.name];
			ulong ENEMY_PIECE = data.capturedName != null ? bitboards[1 - data.playerID][data.capturedName.Value] : 0UL;
			var from = data.from.ToMailBoxIndex();
			var to = data.to.ToMailBoxIndex();

			if (!undo)
			{
				#region DO
				PIECE = PIECE.ClearBit(data.from);
				mailBox[from.x][from.y] = null;

				#region Đặt quân vào ô vị trí {to}
				mailBox[to.x][to.y] = (data.playerID, data.promotedName != null ? data.promotedName.Value : data.name);
				if (data.promotedName != null)
					bitboards[data.playerID][data.promotedName.Value] = bitboards[data.playerID][data.promotedName.Value].SetBit(data.to);
				else PIECE = PIECE.SetBit(data.to);
				#endregion

				#region Xử lý nếu có bắt quân đối phương
				if (data.capturedName != null)
					if (data.enpassantCapturedIndex != null)
					{
						int bitIndex = data.enpassantCapturedIndex.Value;
						ENEMY_PIECE = ENEMY_PIECE.ClearBit(bitIndex);
						var index = bitIndex.ToMailBoxIndex();
						mailBox[index.x][index.y] = null;
					}
					else ENEMY_PIECE = ENEMY_PIECE.ClearBit(data.to);
				#endregion

				if (data.castling != MoveData.Castling.None)
				{
					var r = CASTLING_ROOK_MOVEMENTS[data.playerID][data.castling];
					ulong R = bitboards[data.playerID][PieceName.Rook];
					R = R.ClearBit(r.from);
					mailBox[r.m_from.x][r.m_from.y] = null;
					R = R.SetBit(r.to);
					mailBox[r.m_to.x][r.m_to.y] = (data.playerID, PieceName.Rook);
					bitboards[data.playerID][PieceName.Rook] = R;
				}
				#endregion
			}
			else
			{
				#region UNDO
				PIECE = PIECE.SetBit(data.from);
				mailBox[from.x][from.y] = (data.playerID, data.name);

				#region Lấy quân {data.name} hoặc {data.promotedName} ra khỏi ô vị trí {to}
				if (data.promotedName != null)
					bitboards[data.playerID][data.promotedName.Value] = bitboards[data.playerID][data.promotedName.Value].ClearBit(data.to);
				else PIECE = PIECE.ClearBit(data.to);
				#endregion

				#region Khôi phục lại quân đối phương bị bắt nếu có
				if (data.capturedName != null)
				{
					if (data.enpassantCapturedIndex != null)
					{
						mailBox[to.x][to.y] = null;
						var bitIndex = data.enpassantCapturedIndex.Value;
						ENEMY_PIECE = ENEMY_PIECE.SetBit(bitIndex);
						var index = bitIndex.ToMailBoxIndex();
						mailBox[index.x][index.y] = (1 - data.playerID, PieceName.Pawn);
					}
					else
					{
						ENEMY_PIECE = ENEMY_PIECE.SetBit(data.to);
						mailBox[to.x][to.y] = (1 - data.playerID, data.capturedName.Value);
					}
				}
				else mailBox[to.x][to.y] = null;
				#endregion

				if (data.castling != MoveData.Castling.None)
				{
					var r = CASTLING_ROOK_MOVEMENTS[data.playerID][data.castling];
					ulong R = bitboards[data.playerID][PieceName.Rook];
					R = R.ClearBit(r.to);
					mailBox[r.m_to.x][r.m_to.y] = null;
					R = R.SetBit(r.from);
					mailBox[r.m_from.x][r.m_from.y] = (data.playerID, PieceName.Rook);
					bitboards[data.playerID][PieceName.Rook] = R;
				}
				#endregion
			}

			bitboards[data.playerID][data.name] = PIECE;
			if (data.capturedName != null) bitboards[1 - data.playerID][data.capturedName.Value] = ENEMY_PIECE;
		}
		#endregion
	}



	public static class Extensions
	{
		private static readonly int[][] MAILBOX_TO_BIT = new int[8][];
		private static readonly Vector2Int[] BIT_TO_MAILBOX = new Vector2Int[64];


		static Extensions()
		{
			for (int x = 0; x < 8; ++x) MAILBOX_TO_BIT[x] = new int[8];
			for (int bit = 0, y = 0; y < 8; ++y)
				for (int x = 0; x < 8; ++x, ++bit)
				{
					MAILBOX_TO_BIT[x][y] = bit;
					BIT_TO_MAILBOX[bit] = new Vector2Int(x, y);
				}

			for (int i = 0; i < 64; ++i) NOT_1_LS[i] = ~(_1_LS[i] = 1UL << i);
		}


		/// <summary>
		///  <c>_1_LS[index] == 1UL &lt;&lt; index</c>
		/// </summary>
		private static readonly ulong[] _1_LS = new ulong[64];

		/// <summary>
		/// NOT_1_LS[index] == ~<see cref="_1_LS"/>
		/// </summary>
		private static readonly ulong[] NOT_1_LS = new ulong[64];


		/// <summary>
		/// Return bit[index]. Other bits will be zero.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong GetBit(this ulong u, int index) => u & _1_LS[index];


		/// <summary>
		/// <c>bit[index] == 1</c>
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsBit1(this ulong u, int index) => (u & _1_LS[index]) != 0;


		/// <summary>
		/// Bit[index] is changed to 1. Other bits will be preserved.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong SetBit(this ulong u, int index) => u | _1_LS[index];


		/// <summary>
		/// Bit[index] is changed to 0. Other bits will be preserved.
		/// </summary>
		/// <param name="u"></param>
		/// <param name="index"></param>
		/// <returns></returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong ClearBit(this ulong u, int index) => u & NOT_1_LS[index];


		/// <summary>
		/// Number of bits which is set (bit 1)
		/// </summary>
		public static int Bit1Count(this ulong u)
		{
			int count = 0;
			while (u != 0)
			{
				++count;
				u &= u - 1;
			}

			return count;
		}


		/// <summary>
		/// Nghịch đảo chuỗi bit trong đoạn [startIndex, stopIndex]
		/// <para>Chú ý: index của chuỗi bit ngược với thứ tự của Integral Litteral (Integral Litteral cùng thứ tự với <see cref="string"/> đại diện) !</para>
		/// </summary>
		/// <param name="startIndex">Vị trí bắt đầu (Inclusive)</param>
		/// <param name="stopIndex">Vị trí kết thúc (Inclusive)</param>
		public static ulong ReverseBit(this ulong source, int startIndex, int stopIndex)
		{
			ulong tmp = source;

			#region tmp: xóa những bit ngoài [startIndex, stopIndex]; source: xóa những bit trong [startIndex, stopIndex]
			ulong mask = 0UL;
			for (int i = startIndex; i <= stopIndex; ++i) mask |= _1_LS[i];     // 0...0[1...1]0...0
			tmp &= mask;
			mask = ~mask;                                                       // 1...1[0...0]1...1
			source &= mask;
			#endregion

			ulong reversed = 0UL;

			#region Lấy bit từ tmp và nghịch đảo vị trí rồi đưa vào result
			int length = stopIndex - startIndex + 1;
			int STEP = length / 2;
			if (length % 2 != 0) reversed |= tmp & _1_LS[startIndex + STEP];

			for (int i = 0, L = stopIndex, R = startIndex; i < STEP; ++i, --L, ++R)
			{
				int distance = L - R;
				reversed |= (tmp & _1_LS[L]) >> distance;
				reversed |= (tmp & _1_LS[R]) << distance;
			}
			#endregion

			return source | reversed;
		}


		#region Bit1_To_Index
		private static readonly List<int> listInt = new(64);

		/// <summary>
		/// Chuyển tọa độ các bit 1 sang tọa độ Bit Index.
		/// </summary>
		public static int[] Bit1_To_Index(this ulong bitboard)
		{
			listInt.Clear();
			for (int i = 0; i < 64; ++i) if (bitboard.GetBit(i) != 0) listInt.Add(i);
			return listInt.ToArray();
		}
		#endregion


		/// <summary>
		/// Return: <see cref="string"/> dạng binary.
		/// </summary>
		public static string ToBinary(this ulong u)
		{
			char[] chars = new char[64];
			for (int x = 0; x < 64; ++x) chars[x] = '0';
			char[] INT_CHAR = new char[] { '0', '1' };
			int index = 0;
			while (u != 0)
			{
				chars[index++] = INT_CHAR[u % 2];
				u /= 2;
			}

			Array.Reverse(chars);
			return new(chars);
		}


		/// <summary>
		/// In ra binary <see cref="string"/> với màu sắc: bit 1 là màu đỏ
		/// </summary>
		public static void PrintColorBinary(this ulong u)
		{
			string bin = ToBinary(u);
			for (int i = 0; i < bin.Length; ++i)
			{
				char c = bin[i];
				Console.ForegroundColor = c == '1' ? ConsoleColor.Red : ConsoleColor.White;
				Console.Write(c);
				if (i < bin.Length - 1 && (i + 1) % 4 == 0) Console.Write('_');
			}

			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine();
		}


		/// <summary>
		/// Return: <see cref="string"/> dạng binary trong bàn cờ Vua 8x8.
		/// </summary>
		public static string ToBinary8x8(this ulong u)
		{
			string bin = u.ToBinary();
			string result = "";
			for (int y = 7, start = 7; y >= 0; --y, start += 8)
			{
				result += $"{y + 1}  ";
				for (int x = 0; x < 8; ++x) result += $"  {bin[start - x]}  ";
				result += "\n\n";
			}
			return result + "\n     A    B    C    D    E    F    G    H    \n";
		}


		/// <summary>
		/// In ra nhị phân dạng bàn cờ Vua 8x8 có màu sắc. bit 1 = màu đỏ
		/// </summary>
		public static void PrintColorBinary8x8(this ulong u)
		{
			string bin = u.ToBinary();
			for (int y = 7, start = 7; y >= 0; --y, start += 8)
			{
				Console.ForegroundColor = ConsoleColor.DarkRed;
				Console.Write($"{y + 1}  ");
				for (int x = 0; x < 8; ++x)
				{
					char c = bin[start - x];
					Console.ForegroundColor = c == '1' ? ConsoleColor.Red : ConsoleColor.White;
					Console.Write($"  {c}  ");
				}

				Console.Write("\n\n");
			}

			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("\n     A    B    C    D    E    F    G    H    ");
			Console.ForegroundColor = ConsoleColor.White;
		}


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


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int ToBitIndex(this Vector2Int mailBoxIndex) => MAILBOX_TO_BIT[mailBoxIndex.x][mailBoxIndex.y];


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Vector2Int ToMailBoxIndex(this int bitIndex) => BIT_TO_MAILBOX[bitIndex];


		#region Bit1_To_MailBox
		private static readonly List<Vector2Int> listXY = new(64);

		/// <summary>
		/// Chuyển tọa độ các bit 1 sang tọa độ MailBox.
		/// </summary>
		public static Vector2Int[] Bit1_To_MailBox(this ulong bitboard)
		{
			listXY.Clear();
			for (int i = 0; i < 64; ++i) if (bitboard.GetBit(i) != 0) listXY.Add(i.ToMailBoxIndex());
			return listXY.ToArray();
		}
		#endregion
	}
}