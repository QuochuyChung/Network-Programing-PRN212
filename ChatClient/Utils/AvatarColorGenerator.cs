namespace ChatClient.Utils;

public static class AvatarColorGenerator
{
    private static readonly string[] Palette =
    [
        "#4F46E5", // Indigo
        "#0EA5E9", // Sky
        "#10B981", // Emerald
        "#F59E0B", // Amber
        "#EC4899", // Pink
        "#8B5CF6", // Purple
        "#06B6D4", // Cyan
        "#F97316", // Orange
        "#14B8A6", // Teal
        "#6366F1", // Violet
        "#E11D48"  // Rose
    ];

    public static string GetColorForName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "#64748B";

        int hash = 0;
        foreach (char c in name)
        {
            hash = (hash * 31 + c) & 0x7FFFFFFF;
        }

        return Palette[hash % Palette.Length];
    }
}
