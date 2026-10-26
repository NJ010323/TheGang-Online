using System.Runtime.InteropServices;

/// <summary>Dernier recours lorsque le jeu ne peut pas être relancé après une panne de restauration.</summary>
internal static class NativeMessage
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(nint owner, string text, string caption, uint type);

    public static void Show(string text) => MessageBox(0, text, "The Gang — Mise à jour", 0x10);
}
