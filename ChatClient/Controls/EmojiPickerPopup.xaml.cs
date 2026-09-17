using System.Windows;
using System.Windows.Controls;

namespace ChatClient.Controls;

public partial class EmojiPickerPopup : UserControl
{
    public event Action<string>? EmojiSelected;

    private static readonly string[] Smileys =
    [
        "😀", "😃", "😄", "😁", "😆", "😅", "🤣", "😂", "🙂", "🙃", "😉", "😊", "😇", "🥰", "😍", "🤩",
        "😘", "😗", "😋", "😛", "😜", "🤪", "😝", "🤑", "🤗", "🤭", "🤫", "🤔", "🤐", "🤨", "😐", "😑",
        "😶", "😏", "😒", "🙄", "😬", "🤥", "😌", "😔", "😪", "🤤", "😴", "😷", "🤒", "🤕", "🤢", "🤮",
        "🥵", "🥶", "🥴", "😵", "🤯", "🤠", "🥳", "😎", "🤓", "🧐", "😕", "😟", "🙁", "😮", "😯", "😲",
        "😳", "🥺", "😦", "😧", "😨", "😰", "😥", "😢", "😭", "😱", "😖", "😣", "😞", "😓", "😩", "😫"
    ];

    private static readonly string[] Gestures =
    [
        "👋", "🤚", "🖐️", "✋", "🖖", "👌", "🤌", "🤏", "✌️", "🤞", "🤟", "🤘", "🤙", "👈", "👉", "👆",
        "👇", "☝️", "👍", "👎", "✊", "👊", "🤛", "🤜", "👏", "🙌", "👐", "🤲", "🤝", "🙏", "✍️", "💪"
    ];

    private static readonly string[] Hearts =
    [
        "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍", "🤎", "💔", "❣️", "💕", "💞", "💓", "💗", "💖",
        "💘", "💝", "💟", "⭐", "🌟", "✨", "⚡", "🔥", "💥", "💯", "💢", "💨", "💫", "🕊️", "☮️", "✅"
    ];

    private static readonly string[] Celebrations =
    [
        "🎉", "🎊", "🎈", "🎂", "🎁", "🎀", "🎇", "🎆", "🧨", "🎃", "🎄", "🧧", "🎫", "🏆", "🥇", "🥈",
        "🥉", "🏅", "🎖️", "⚽", "🏀", "🏈", "⚾", "🎾", "🏐", "🎱", "🏓", "🏸", "🥊", "🎯", "🎮", "🎲"
    ];

    private static readonly string[] Animals =
    [
        "🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼", "🐨", "🐯", "🦁", "🐮", "🐷", "🐸", "🐵", "🐔",
        "🐧", "🐦", "🐤", "🦆", "🦅", "🦉", "🦇", "🐺", "🐗", "🐴", "🦄", "🐝", "🦋", "🐌", "🐞", "🐢"
    ];

    private static readonly string[] Foods =
    [
        "🍏", "🍎", "🍐", "🍊", "🍋", "🍌", "🍉", "🍇", "🍓", "🍒", "🍑", "🥭", "🍍", "🥥", "🥝", "🍅",
        "🥑", "🥦", "🌽", "🌶️", "🥐", "🍞", "🧀", "🍳", "🥞", "🥓", "🥩", "🍗", "🍔", "🍟", "🍕", "🍜"
    ];

    public EmojiPickerPopup()
    {
        InitializeComponent();
        LoadCategory(Smileys);
    }

    private void LoadCategory(string[] emojis)
    {
        if (EmojiContainer == null) return;
        EmojiContainer.Children.Clear();
        var style = (Style)FindResource("EmojiItemButtonStyle");

        foreach (var emoji in emojis)
        {
            var btn = new Button
            {
                Style = style,
                Tag = emoji
            };

            var textBlock = new Emoji.Wpf.TextBlock
            {
                Text = emoji,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            btn.Content = textBlock;
            btn.Click += (s, e) =>
            {
                EmojiSelected?.Invoke(emoji);
            };

            EmojiContainer.Children.Add(btn);
        }
    }

    private void OnTabSmileysChecked(object sender, RoutedEventArgs e) => LoadCategory(Smileys);
    private void OnTabGesturesChecked(object sender, RoutedEventArgs e) => LoadCategory(Gestures);
    private void OnTabHeartsChecked(object sender, RoutedEventArgs e) => LoadCategory(Hearts);
    private void OnTabCelebrationChecked(object sender, RoutedEventArgs e) => LoadCategory(Celebrations);
    private void OnTabAnimalsChecked(object sender, RoutedEventArgs e) => LoadCategory(Animals);
    private void OnTabFoodChecked(object sender, RoutedEventArgs e) => LoadCategory(Foods);
}
