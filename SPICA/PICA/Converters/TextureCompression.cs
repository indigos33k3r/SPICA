﻿using SPICA.Utils;

using System;
using System.Drawing;
using System.IO;

namespace SPICA.PICA.Converters
{
    static class TextureCompression
    {
        //TODO: Alpha is probably wrong for ETC1A4
        private static byte[] XT = { 0, 4, 0, 4 };
        private static byte[] YT = { 0, 0, 4, 4 };

        public static byte[] ETC1Decompress(byte[] Input, int Width, int Height, bool Alpha, bool SwapRB)
        {
            byte[] Output = new byte[Width * Height * 4];

            using (MemoryStream MS = new MemoryStream(Input))
            {
                BinaryReader Reader = new BinaryReader(MS);

                for (int TY = 0; TY < Height; TY += 8)
                {
                    for (int TX = 0; TX < Width; TX += 8)
                    {
                        for (int T = 0; T < 4; T++)
                        {
                            ulong AlphaBlock = 0xfffffffffffffffful;

                            if (Alpha) AlphaBlock = Reader.ReadUInt64();

                            ulong ColorBlock = BitUtils.Swap64(Reader.ReadUInt64());

                            byte[] Tile = ETC1Tile(ColorBlock);

                            int TileOffset = 0;

                            for (int PY = YT[T]; PY < 4 + YT[T]; PY++)
                            {
                                for (int PX = XT[T]; PX < 4 + XT[T]; PX++)
                                {
                                    int OOffs = ((TY + PY) * Width + TX + PX) * 4;

                                    Buffer.BlockCopy(Tile, TileOffset, Output, OOffs, 3);

                                    byte A = (byte)((AlphaBlock >> TileOffset) & 0xf);

                                    Output[OOffs + 3] = (byte)((A << 4) | A);

                                    if (SwapRB)
                                    {
                                        byte Temp = Output[OOffs + 0];

                                        Output[OOffs + 0] = Output[OOffs + 2];
                                        Output[OOffs + 2] = Temp;
                                    }

                                    TileOffset += 4;
                                }
                            }
                        }
                    }
                }

                return Output;
            }
        }

        private static byte[] ETC1Tile(ulong Block)
        {
            uint BlockLow = (uint)(Block >> 32);
            uint BlockHigh = (uint)Block;

            bool Flip = (BlockHigh & 0x1000000) != 0;
            bool Diff = (BlockHigh & 0x2000000) != 0;

            uint R1, G1, B1;
            uint R2, G2, B2;

            if (Diff)
            {
                R1 = (BlockHigh & 0x0000f8) >> 0;
                G1 = (BlockHigh & 0x00f800) >> 8;
                B1 = (BlockHigh & 0xf80000) >> 16;

                R2 = (uint)((sbyte)(R1 >> 3) + ((sbyte)((BlockHigh & 0x000007) << 5) >> 5));
                G2 = (uint)((sbyte)(G1 >> 3) + ((sbyte)((BlockHigh & 0x000700) >> 3) >> 5));
                B2 = (uint)((sbyte)(B1 >> 3) + ((sbyte)((BlockHigh & 0x070000) >> 11) >> 5));

                R1 |= R1 >> 5;
                G1 |= G1 >> 5;
                B1 |= B1 >> 5;

                R2 = (R2 << 3) | (R2 >> 2);
                G2 = (G2 << 3) | (G2 >> 2);
                B2 = (B2 << 3) | (B2 >> 2);
            }
            else
            {
                R1 = (BlockHigh & 0x0000f0) >> 0;
                G1 = (BlockHigh & 0x00f000) >> 8;
                B1 = (BlockHigh & 0xf00000) >> 16;

                R2 = (BlockHigh & 0x00000f) << 4;
                G2 = (BlockHigh & 0x000f00) >> 4;
                B2 = (BlockHigh & 0x0f0000) >> 12;

                R1 |= R1 >> 4;
                G1 |= G1 >> 4;
                B1 |= B1 >> 4;

                R2 |= R2 >> 4;
                G2 |= G2 >> 4;
                B2 |= B2 >> 4;
            }

            uint Table1 = (BlockHigh >> 29) & 7;
            uint Table2 = (BlockHigh >> 26) & 7;

            byte[] Output = new byte[4 * 4 * 4];

            if (!Flip)
            {
                for (int Y = 0; Y < 4; Y++)
                {
                    for (int X = 0; X < 2; X++)
                    {
                        Color Color1 = ETC1Pixel(R1, G1, B1, X + 0, Y, BlockLow, Table1);
                        Color Color2 = ETC1Pixel(R2, G2, B2, X + 2, Y, BlockLow, Table2);

                        int Offset1 = (Y * 4 + X) * 4;

                        Output[Offset1 + 0] = Color1.B;
                        Output[Offset1 + 1] = Color1.G;
                        Output[Offset1 + 2] = Color1.R;

                        int Offset2 = (Y * 4 + X + 2) * 4;

                        Output[Offset2 + 0] = Color2.B;
                        Output[Offset2 + 1] = Color2.G;
                        Output[Offset2 + 2] = Color2.R;
                    }
                }
            }
            else
            {
                for (int Y = 0; Y < 2; Y++)
                {
                    for (int X = 0; X < 4; X++)
                    {
                        Color Color1 = ETC1Pixel(R1, G1, B1, X, Y + 0, BlockLow, Table1);
                        Color Color2 = ETC1Pixel(R2, G2, B2, X, Y + 2, BlockLow, Table2);

                        int Offset1 = (Y * 4 + X) * 4;

                        Output[Offset1 + 0] = Color1.B;
                        Output[Offset1 + 1] = Color1.G;
                        Output[Offset1 + 2] = Color1.R;

                        int Offset2 = ((Y + 2) * 4 + X) * 4;

                        Output[Offset2 + 0] = Color2.B;
                        Output[Offset2 + 1] = Color2.G;
                        Output[Offset2 + 2] = Color2.R;
                    }
                }
            }

            return Output;
        }

        private static int[,] ETC1LUT =
        {
            {    2,   8,    -2,   -8 },
            {    5,   17,   -5,  -17 },
            {    9,   29,   -9,  -29 },
            {   13,   42,  -13,  -42 },
            {   18,   60,  -18,  -60 },
            {   24,   80,  -24,  -80 },
            {   33,  106,  -33, -106 },
            {   47,  183,  -47, -183 }
        };

        private static Color ETC1Pixel(uint R, uint G, uint B, int X, int Y, uint Block, uint Table)
        {
            int index = X * 4 + Y;
            uint MSB = Block << 1;

            int Pixel = index < 8
                ? ETC1LUT[Table, ((Block >> (index + 24)) & 1) + ((MSB >> (index + 8)) & 2)]
                : ETC1LUT[Table, ((Block >> (index + 8)) & 1) + ((MSB >> (index - 8)) & 2)];

            R = Saturate((int)(R + Pixel));
            G = Saturate((int)(G + Pixel));
            B = Saturate((int)(B + Pixel));

            return Color.FromArgb((int)R, (int)G, (int)B);
        }

        private static byte Saturate(int Value)
        {
            if (Value > byte.MaxValue) return byte.MaxValue;
            if (Value < byte.MinValue) return byte.MinValue;

            return (byte)Value;
        }
    }
}
