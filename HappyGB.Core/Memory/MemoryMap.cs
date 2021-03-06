using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using HappyGB.Core.Cpu;
using HappyGB.Core.Graphics;
using HappyGB.Core.Input;

namespace HappyGB.Core.Memory
{
    /// <summary>
    /// Defines a gameboy's internal memory map.
    /// 
    /// FIXME: This is totally not testable.
    /// </summary>
    public class MemoryMap
        : IMemoryMap
    {
        private byte[] internalRam;
        private byte[] highRam;
        private byte[] bios;
        private byte p1State;

        public byte IE { get; set; }
        public byte IF { get; set; }
        public bool BiosEnabled { get; private set; }

        //FIXME: Magic setter.
        /// <summary>
        /// Set to perform a DMA copy.
        /// </summary>
        private byte DMA
        {
            set {
                ushort addr = value;
                addr = (ushort)(addr << 8);
                for(ushort i = 0; i < 0xA0; i++)
                    gfx.WriteOAM8((ushort)(0xFE00 + i),
                        this[(ushort)(addr + i)]);
            }
        }

        /// <summary>
        /// Stores input.
        /// Set to read input from A/B/Sel/Start, or Up/Down/Left/Right
        /// </summary>
        private byte P1
        {
            get
            {
                int val = 0;

                if (p1State == 0)
                    return 0;

                else if ((p1State & 0x20) == 0x20)
                {
                    if (!input.GetInputState(GameboyKey.A))
                        val |= 0x01;
                    if (!input.GetInputState(GameboyKey.B))
                        val |= 0x02;
                    if (!input.GetInputState(GameboyKey.Select))
                        val |= 0x04;
                    if (!input.GetInputState(GameboyKey.Start))
                        val |= 0x08;
                    return (byte)(val | p1State);
                }
                else
                {
                    if (!input.GetInputState(GameboyKey.Right))
                        val |= 0x01;
                    if (!input.GetInputState(GameboyKey.Left))
                        val |= 0x02;
                    if (!input.GetInputState(GameboyKey.Up))
                        val |= 0x04;
                    if (!input.GetInputState(GameboyKey.Down))
                        val |= 0x08;
                    return (byte)(val | p1State);
                }
            }
            set
            {
                p1State = value;
            }
        }

        private IMemoryBankController cart;
        private GraphicsController gfx;
        private TimerController timer;
        private IInputProvider input;

        public byte this[ushort addr]
        {	
            //TODO: This is kinda slow. See what we can do about it.
            get 
            {
                //Perform memory mapping stuff here.
                if (addr < 0x8000)
                    if (BiosEnabled && (addr < 256))
                        return bios[addr];
                    else return cart.Read8(addr); //Cart
                else if (addr < 0xA000)
                    return gfx.ReadVRAM8(addr);
                else if (addr < 0xC000)
                    return cart.Read8(addr);
                else if (addr < 0xE000)
                    return internalRam[addr - 0xC000];
                else if (addr < 0xFE00)
                    return internalRam[addr - 0xE000];
                else if (addr < 0xFEA0)
                    return gfx.ReadOAM8(addr); //FIXME: Dead zone is mapped to OAM.
                else if (addr < 0xFEFF)
                    return 0;
                else 
                {
                    //IO and HighRam.
                    switch (addr & 0x00FF) {
                    case 0xFF:
                        return IE;
                    case 0x0F:
                        return IF;

                    //Joypad P1
                    case 0x00:
                        return P1; 

                    ///Timer
                    case 0x04: 
                        return timer.DIV;
                    case 0x05: 
                        return timer.TIMA;
                    case 0x06: 
                        return timer.TMA;
                    case 0x07: 
                        return timer.TAC;

                    ///Graphics.
                    case 0x40: 
                        return gfx.LCDC;
                    case 0x41: 
                        return gfx.STAT;
                    case 0x42:
                        return gfx.SCY;
                    case 0x43:
                        return gfx.SCX;
                    case 0x44:
                        return gfx.LY;
                    case 0x45:
                        return gfx.LYC;
                    case 0x47:
                        return gfx.BGP;
                    case 0x48:
                        return gfx.OBP0;
                    case 0x49:
                        return gfx.OBP1;
                    case 0x4A:
                        return gfx.WY;
                    case 0x4B:
                        return gfx.WX;
                    default:
                        if(addr < 0xFF80)
                            return 0;
                        return highRam[addr - 0xFF80];
                    }
                }
            }

            set 
            { 
                //Perform memory mapping stuff here.
                if (addr < 0x8000)
                {
                    //No bios write necessary.
                    cart.Write8(addr, value); //Cart
                }
                else if (addr < 0xA000)
                    gfx.WriteVRAM8(addr, value);
                else if (addr < 0xC000)
                    cart.Write8(addr, value);
                else if (addr < 0xE000)
                    internalRam[addr - 0xC000] = value;
                else if (addr < 0xFE00)
                    internalRam[addr - 0xE000] = value;
                else if (addr < 0xFEA0)
                    gfx.WriteOAM8(addr, value);
                else if (addr < 0xFEFF)
                //System.Diagnostics.Debug.WriteLine("Writing to junk address." + addr.ToString("X"));
                { }
                else
                {
                    //IO and HighRam.
                    switch (addr & 0x00FF)
                    {
                        case 0x00:
                            P1 = value;
                            break;

                        //serial log
                        case 0x01:
                            //System.Diagnostics.Debug.Write((char)value);
                            this.DebugSerialOut((char)value);
                            break;

                        case 0xFF:
                            IE = value;
                            break;
                        case 0x0F:
                            IF = value;
                            break;

                        ///Timer
                        case 0x04:
                            timer.DIV = value;
                            break;
                        case 0x05:
                            //timer.TIMA = value;
                            break;
                        case 0x06:
                            timer.TMA = value;
                            break;
                        case 0x07:
                            timer.TAC = value;
                            break;

                        ///Graphics
                        case 0x40:
                            gfx.LCDC = value;
                            break;
                        case 0x41:
                            gfx.STAT = value;
                            break;
                        case 0x42:
                            gfx.SCY = value;
                            break;
                        case 0x43:
                            gfx.SCX = value;
                            break;
                        case 0x45:
                            gfx.LYC = value;
                            break;
                        case 0x46:
                            DMA = value;
                            break;
                        case 0x47:
                            gfx.BGP = value;
                            break;
                        case 0x48:
                            gfx.OBP0 = value;
                            break;
                        case 0x49:
                            gfx.OBP1 = value;
                            break;
                        case 0x4A:
                            gfx.WY = value;
                            break;
                        case 0x4B:
                            gfx.WX = value;
                            break;

                        //Bios
                        case 0x50:
                            if (value == 0x01)
                                BiosEnabled = false;
                            break;

                        default:
                            if (addr < 0xFF80) // IO port but not mapped.
                            { /* ??? */}
                            else highRam[addr - 0xFF80] = value; //or highram.
                            break;
                    }
                }
            }
        }

        public MemoryMap(IMemoryBankController cart, GraphicsController gfx, TimerController timer, IInputProvider input)
        {
            internalRam = new byte[0x2000];
            highRam = new byte[0x80];
            this.cart = cart;
            this.gfx = gfx;
            this.timer = timer;
            BiosEnabled = true;

            this.input = input;

            try
            {
                using (var file = File.Open(@"DMG_ROM.bin", FileMode.Open, FileAccess.Read))
                using (BinaryReader r = new BinaryReader(file))
                {
                    bios = r.ReadBytes(256);
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("No bios found, skipping.");
                BiosEnabled = false;
            }
        }

        [Conditional("DEBUG")]
        private void DebugSerialOut(char value)
        {
            Debug.Write(value);
        }
    }
}

