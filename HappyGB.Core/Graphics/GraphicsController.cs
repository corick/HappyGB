using System;
using HappyGB.Core.Cpu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HappyGB.Core.Graphics
{
    public class GraphicsController
    {
        private bool lcdcEqual, lcdcOamAccess, lcdcVblank, lcdcHblank;
        private const int SCANLINE_INTERVAL = 456;
        private byte[] vram;
        private byte[] oam;

        private int scanline;
        private int clock;

        public enum LCDState
            : byte
        {
            HBlank = 0x00,
            VBlank = 0x01,
            OamAccess = 0x02,
            LCDCopy = 0x03
        }

        private LCDState state;

        //FIXME: Should be private; Hack to make VramViewer work.
        public GbPalette obp0, obp1, bgp;

        private ISurface buffer;

        public ISurface Surface
        {
            get { return buffer; }
        }

        //TODO: Registers.
        /// <summary>
        /// The LCD Control register.
        /// </summary>
        public byte LCDC
        {
            get;
            set;
        }

        /// <summary>
        /// The LCD Status register.
        /// </summary>
        public byte STAT 
        {
            get
            {
                int res = 0;
                res |= lcdcEqual ? 0x40 : 0x00;
                res |= lcdcOamAccess ? 0x20 : 0x00;
                res |= lcdcVblank ? 0x10 : 0x00;
                res |= lcdcHblank ? 0x08 : 0x00;
                res |= (LY == LYC) ? 0x04 : 0x00;
                res |= (byte)state;
                return (byte)(res & 0xFF);
            }

            set
            {
                lcdcEqual = ((value & 0x40) == 0x40);
                lcdcOamAccess = ((value & 0x20) == 0x20);
                lcdcVblank = ((value & 0x10) == 0x10);
                lcdcHblank = ((value & 0x08) == 0x08);
            }
        }

        private const short SCREEN_WIDTH = 160;
        private const short TILEMAP_SIZE = 32;
        private const short TILE_PX = 8;

        private const int HBLANK_CYCLES = 207;
        private const int VBLANK_CYCLES = 4560;
        private const int OAM_CYCLES = 83;
        private const int LCDCOPY_CYCLES = 175;


        /// <summary>
        /// Y Scroll amount memory-mapped register.
        /// </summary>
        public byte SCY 
        {
            get;
            set;
        }

        /// <summary>
        /// X Scroll amount memory-mapped register.
        /// </summary>
        public byte SCX
        {
            get;
            set;
        }

        /// <summary>
        /// Memory-mapped register with the current scanline.
        /// </summary>
        public byte LY
        {
            get { return (byte)scanline; }
            set { scanline = 0; } //FIXME: Should this actually do that? That'll f things up! 
        }

        /// <summary>
        /// The LYC memory-mapped register.
        /// This is used for the LY=LYC Coincidence LCDC interrupt.
        /// </summary>
        public byte LYC
        {
            get;
            set;
        }

        //Palettes: Update the SurfaceBlitter.
        public byte BGP 
        {
            get { return bgp.RegisterValue; }
            set { bgp.RegisterValue = value; }
        }

        public byte OBP0 
        {
            get { return obp0.RegisterValue; }
            set { obp0.RegisterValue = value; }

        }

        public byte OBP1 
        {
            get { return obp1.RegisterValue; }
            set { obp1.RegisterValue = value; }
        }

        public byte WY 
        {
            get;
            set;
        }

        public byte WX 
        {
            get;
            set;
        }

        public GraphicsController()
        {
            buffer = new ScreenSurface();
            state = LCDState.OamAccess;
            clock = scanline = 0;
            lcdcHblank = lcdcOamAccess = lcdcEqual = lcdcVblank = false;
            vram = new byte[0x2000];
            oam = new byte[0xA0];

            bgp = new GbPalette(false);
            obp0 = new GbPalette(true);
            obp1 = new GbPalette(true);
        }

        /// <summary>
        /// Updates the graphics controller with the given clock.
        /// </summary>
        public InterruptType Update(int ticks)
        {
            clock += ticks;

            switch (state)
            {
                case LCDState.HBlank: //Mode 0
                    //Are we done with this one yet?
                    if (clock > HBLANK_CYCLES)
                    {
                        if (scanline >= 144) //Should switch to VBlank.
                        {
                            clock = 0;
                            state = LCDState.VBlank;
                            //Raise the VBlank interrupt if enabled.
                            if (lcdcVblank)
                                return InterruptType.VBlank | InterruptType.LCDController;
                            else return InterruptType.VBlank;
                        }
                        else
                        {
                            clock = 0;
                            state = LCDState.OamAccess;
                            scanline++;
                            if (lcdcOamAccess)
                                return InterruptType.LCDController;
                        }
                    }
                    break;
                case LCDState.OamAccess: //Mode 2
                    if (clock > OAM_CYCLES) //Switch to LCD Copy.
                    {
                        clock = 0;
                        state = LCDState.LCDCopy;

                        //Also raise the LCDC interrupt if we have one and it's enabled.
                        if ((LY == LYC) && (lcdcEqual)) 
                        {
                            return InterruptType.LCDController;
                        }
                    }
                    break;
                case LCDState.LCDCopy: //Mode 3
                    if (clock > LCDCOPY_CYCLES) //Switch to HBlank
                    {
                        clock = 0;
                        state = LCDState.HBlank;

                        if(scanline < 144)
                            WriteScanline();

                        //Also raise the HBlank interrupt if it's enabled.
                        if (lcdcHblank) 
                            return InterruptType.LCDController;
                    }
                    break;
                case LCDState.VBlank: //Mode 1
                    scanline = (144 + (clock / SCANLINE_INTERVAL));
                    if (clock > VBLANK_CYCLES)
                    {
                        clock = 0;
                        scanline = 0;
                        state = LCDState.OamAccess;
                        if (lcdcOamAccess)
                            return InterruptType.LCDController;
                    }
                    break;
            }
            //No interrupt!
            return InterruptType.None;
        }

        public byte ReadOAM8(ushort address)
        {
            return oam[address - 0xFE00];
        }

        public void WriteOAM8(ushort address, byte value)
        {
            oam[address - 0xFE00] = value;
        }

        public byte ReadVRAM8(ushort address)
        {
            return vram[address - 0x8000];
        }

        public void WriteVRAM8(ushort address, byte value)
        {
            vram[address - 0x8000] = value;
        }

        /// <summary>
        /// Writes one full scanline to the back buffer.
        /// </summary>
        private unsafe void WriteScanline()
        {
            ushort backAddrBase = ((LCDC & 0x08) == 0x08) ? (ushort)0x9C00 : (ushort)0x9800;
            ushort windAddrBase = ((LCDC & 0x40) == 0x40) ? (ushort)0x9C00 : (ushort)0x9800;
            ushort tileDataBase = ((LCDC & 0x10) == 0x10) ? (ushort)0x8000 : (ushort)0x8800;
            bool signedTileData = backAddrBase == 0x8000;
            bool windowEnabled = ((LCDC & 0x20) == 0x20);

            //Get the start of the first tile in the scanline rel to screen.
            int xscan = -(SCX % TILE_PX);

            //Compute these before.
            int bTileRow = ((SCY + scanline) / TILE_PX) % TILEMAP_SIZE;
            int bTileColBase = ((SCX + xscan) / TILE_PX) % TILEMAP_SIZE;
            int bTileOffset = backAddrBase + (bTileRow * TILEMAP_SIZE) + bTileColBase;

            for (int i = 0; i < 21; i++)  //# of tiles in window + 1. We have to account for edges.
            {
                //get the tile at the x,y.
                //Do BG first.
                ushort dataOffsetAddr = 0;
                if (!signedTileData)
                {
                    byte tileNumber = ReadVRAM8((ushort)(bTileOffset + i));
                    dataOffsetAddr = (ushort)(tileDataBase + 16*tileNumber);
                }
                else
                {
                    sbyte tileNumber = unchecked((sbyte)ReadVRAM8((ushort)(bTileOffset + i)));
                    dataOffsetAddr = (ushort)(tileDataBase + 16*tileNumber);
                }

                //Add scanline to tile offset so we get the correct tile scanline.
                ushort tileDataOffset = (ushort)((2 * (scanline + SCY)) % (TILE_PX * 2));
                dataOffsetAddr += tileDataOffset;

                byte tileHi = ReadVRAM8(dataOffsetAddr);
                byte tileLo = ReadVRAM8((ushort)(dataOffsetAddr + 1));

                //draw that tile to the buffer.
                DrawTileScan(bgp, tileHi, tileLo, xscan, scanline);

                //Now draw window


                //now draw sprites.

                xscan += TILE_PX;
            }
        }

        private void DrawTileScan(GbPalette pal, byte tileHigh, byte tileLow, int x, int y)
        {
            //Put both in the same int so we only have to shift once per thing.
            int bit = (tileHigh << 8) + tileLow;

            int absOffset = (y * SCREEN_WIDTH) + x;

            //Draw 8 pixels.
            for (int pixOffset = 0; pixOffset < 8; pixOffset++)
            {
                //Don't draw off the screen.
                if (x + pixOffset < 0) continue;
                if (x + pixOffset >= SCREEN_WIDTH) continue;

                //Get the color from the tile's data.
                byte paletteColor = 0;
                if ((bit & 0x8000) == 0x8000)
                    paletteColor |= 0x02;
                if ((bit & 0x80) == 0x80)
                    paletteColor |= 0x01;

                var systemColor = pal.GetColor(paletteColor);
                var c = new Color(systemColor.R, systemColor.G, systemColor.B, systemColor.A);

                //Draw to the thing.
                Surface.Buffer[(absOffset + pixOffset)] = c;

                bit = bit << 1;
            }
        }
    }
}

