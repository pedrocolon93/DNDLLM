// Assets/Scripts/UI/UITheme.cs
using UnityEngine;

namespace DnD.UI
{
    public static class UITheme
    {
        // Backgrounds
        public static readonly Color32 BackgroundDeep   = new Color32(0x1A, 0x10, 0x05, 0xFF);
        public static readonly Color32 BackgroundMid    = new Color32(0x1E, 0x15, 0x08, 0xFF);
        public static readonly Color32 BackgroundDM     = new Color32(0x2A, 0x1F, 0x0E, 0xFF);
        public static readonly Color32 BackgroundPlayer = new Color32(0x1A, 0x0F, 0x05, 0xFF);

        // Text
        public static readonly Color32 GoldAccent    = new Color32(0xC8, 0xA0, 0x50, 0xFF);
        public static readonly Color32 DmText        = new Color32(0xD4, 0xB8, 0x7A, 0xFF);
        public static readonly Color32 PlayerText    = new Color32(0xF0, 0xD0, 0x90, 0xFF);
        public static readonly Color32 SystemText    = new Color32(0xA0, 0x80, 0x60, 0xFF);
        public static readonly Color32 InputText     = new Color32(0xE8, 0xD0, 0xA0, 0xFF);
        public static readonly Color32 PlaceholderText = new Color32(0x6B, 0x50, 0x30, 0xFF);

        // Font sizes
        public const float FontHeader  = 13f;
        public const float FontDM      = 16f;
        public const float FontPlayer  = 16f;
        public const float FontSystem  = 13f;
        public const float FontInput   = 15f;
    }
}
