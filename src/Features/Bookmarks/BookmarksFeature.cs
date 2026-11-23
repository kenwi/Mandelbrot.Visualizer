using System.Numerics;
using Mandelbrot.Visualizer.Features.Coloring;
using Mandelbrot.Visualizer.Features.Formula;
using Mandelbrot.Visualizer.Features.Navigation;
using Mandelbrot.Visualizer.Features.Rendering;
using Mandelbrot.Visualizer.Shared;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Bookmarks;

/// <summary>
/// Vertical slice owning curated fractal landmarks and user-saved bookmarks persisted in
/// bookmarks.json, a scrollable dropdown selector, an inline save-with-name prompt modal,
/// and a "Copy CLI" clipboard action.
/// </summary>
public sealed class BookmarksFeature
{
    const int RowHeight = 26;
    const int HeaderHeight = 20;
    const int MaxPopupHeight = 420;
    const int MaxNameLength = 64;

    readonly string _storePath;
    readonly List<BookmarkEntry> _entries;

    int _selectedIndex = -1;
    bool _dropdownOpen;
    float _dropdownScroll;
    Rectangle _triggerRect;
    bool _openedThisFrame;

    bool _saveModalOpen;
    string _nameBuffer = "";
    string _fallbackName = "";
    string _modalSummary = "";
    double _copiedFeedbackUntil;
    double _savedFeedbackUntil;
    string _cycleToastText = "";
    double _cycleToastUntil;

    public BookmarksFeature(string? storePath = null)
    {
        _storePath = string.IsNullOrWhiteSpace(storePath) ? BookmarkStore.ResolveDefaultPath() : storePath;
        _entries = BookmarkStore.LoadOrSeed(_storePath);
    }

    public string StorePath => _storePath;
    public IReadOnlyList<BookmarkEntry> Entries => _entries;
    public int SelectedIndex => _selectedIndex;
    public string? SelectedName => _selectedIndex >= 0 && _selectedIndex < _entries.Count
        ? _entries[_selectedIndex].DisplayName
        : null;
    public bool IsDropdownOpen => _dropdownOpen;
    public bool IsSaveModalOpen => _saveModalOpen;
    public bool CapturesInput => _dropdownOpen || _saveModalOpen;
    public bool CapturesKeyboard => _saveModalOpen;
    public string LastCopiedCommand { get; private set; } = "";

    public void ClearSelection() => _selectedIndex = -1;

    public void DismissPopups()
    {
        _dropdownOpen = false;
        _saveModalOpen = false;
    }

    public void OpenSavePrompt(ViewportState view, FormulaParams formula)
    {
        _dropdownOpen = false;
        _saveModalOpen = true;
        _openedThisFrame = true;
        _nameBuffer = "";
        _fallbackName = BookmarkEntry.BuildFallbackName(view, formula);
        _modalSummary = _fallbackName;
    }

    public string CopyCliCommand(
        ViewportState view,
        FormulaParams formula,
        ColoringParams coloring,
        bool useGpu,
        SsaaMode ssaa)
    {
        string command = CliCommandBuilder.Build(view, formula, coloring, useGpu, ssaa);
        LastCopiedCommand = command;
        Raylib.SetClipboardText(command);
        _copiedFeedbackUntil = Raylib.GetTime() + 2.0;
        return command;
    }

    public BookmarkEntry SaveCurrentSpot(
        string? name,
        ViewportState view,
        FormulaParams formula,
        bool autoIterations,
        ColoringParams coloring)
    {
        BookmarkEntry entry = BookmarkEntry.FromCurrentState(name, view, formula, autoIterations, coloring);
        _entries.Insert(0, entry);
        _selectedIndex = 0;
        BookmarkStore.TrySave(_storePath, _entries);
        return entry;
    }

    public bool DeleteAt(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].Curated)
            return false;

        _entries.RemoveAt(index);
        if (_selectedIndex == index)
            _selectedIndex = -1;
        else if (_selectedIndex > index)
            _selectedIndex--;

        BookmarkStore.TrySave(_storePath, _entries);
        return true;
    }

    public bool ApplyByIndex(
        int index,
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring)
    {
        if (index < 0 || index >= _entries.Count)
            return false;

        BookmarkEntry entry = _entries[index];
        if (!entry.TryParseView(out BigFloat cx, out BigFloat cy, out FloatExp scale))
            return false;

        formula.SetAutoIterations(entry.AutoIterations);
        formula.SetFormula(entry.ToFormulaParams());
        coloring.SetColoring(entry.ToColoringParams());
        navigation.SetExact(cx, cy, scale);
        navigation.CancelPointerInteraction();
        _selectedIndex = index;
        return true;
    }

    public bool ApplyByName(
        string name,
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string target = name.Trim();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Name.Equals(target, StringComparison.OrdinalIgnoreCase))
                return ApplyByIndex(i, navigation, formula, coloring);
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Name.Contains(target, StringComparison.OrdinalIgnoreCase))
                return ApplyByIndex(i, navigation, formula, coloring);
        }

        return false;
    }

    public bool Cycle(
        int delta,
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring)
    {
        if (_entries.Count == 0 || delta == 0)
            return false;

        int nextIndex = _selectedIndex < 0
            ? (delta > 0 ? 0 : _entries.Count - 1)
            : ((_selectedIndex + delta) % _entries.Count + _entries.Count) % _entries.Count;

        if (!ApplyByIndex(nextIndex, navigation, formula, coloring))
            return false;

        _cycleToastText = $"{_entries[nextIndex].DisplayName}  ({nextIndex + 1}/{_entries.Count})";
        _cycleToastUntil = Raylib.GetTime() + 2.2;
        return true;
    }

    public bool HandleKeys(
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_B))
        {
            OpenSavePrompt(navigation.State, formula.State);
            return false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_PERIOD) || Raylib.IsKeyPressed(KeyboardKey.KEY_PAGE_DOWN))
            return Cycle(+1, navigation, formula, coloring);

        if (Raylib.IsKeyPressed(KeyboardKey.KEY_COMMA) || Raylib.IsKeyPressed(KeyboardKey.KEY_PAGE_UP))
            return Cycle(-1, navigation, formula, coloring);

        return false;
    }

    public void DrawPanel(
        ref PanelLayout layout,
        ViewportState view,
        FormulaParams formula,
        ColoringParams coloring,
        RenderingFeature rendering,
        TextEditScope editScope)
    {
        const float labelWidth = 90f;
        RayGui.GuiLabel(new Rectangle { x = layout.X, y = layout.Y, width = labelWidth, height = 28 }, "Bookmark");
        _triggerRect = new Rectangle
        {
            x = layout.X + labelWidth,
            y = layout.Y,
            width = layout.Width - labelWidth,
            height = 28,
        };
        layout.Y += 34;

        bool hovered = !_saveModalOpen && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _triggerRect);
        Color bg = _dropdownOpen
            ? new Color(38, 48, 68, 255)
            : hovered
                ? new Color(36, 44, 62, 255)
                : new Color(30, 34, 46, 255);
        Color border = _dropdownOpen || hovered ? new Color(96, 176, 232, 255) : PanelTheme.PanelBorder;

        Raylib.DrawRectangleRec(_triggerRect, bg);
        Raylib.DrawRectangleLinesEx(_triggerRect, 1f, border);

        string label = _selectedIndex >= 0 && _selectedIndex < _entries.Count
            ? _entries[_selectedIndex].DisplayName
            : "Landmarks & saved";
        string clipped = FitText(label, (int)_triggerRect.width - 28, 15);
        Raylib.DrawText(clipped, (int)_triggerRect.x + 8, (int)_triggerRect.y + 6, 15, PanelTheme.TitleText);
        Raylib.DrawText(_dropdownOpen ? "^" : "v", (int)(_triggerRect.x + _triggerRect.width - 16), (int)_triggerRect.y + 6, 15, PanelTheme.MutedText);

        if (!_saveModalOpen && hovered && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT))
        {
            editScope.CancelAll();
            _dropdownOpen = !_dropdownOpen;
            if (_dropdownOpen)
            {
                _openedThisFrame = true;
                _dropdownScroll = Math.Max(0f, _dropdownScroll);
            }
        }

        double now = Raylib.GetTime();
        string saveLabel = now < _savedFeedbackUntil ? "Saved!" : "Save spot";
        string copyLabel = now < _copiedFeedbackUntil ? "Copied!" : "Copy CLI";

        if (_dropdownOpen || _saveModalOpen)
            RayGui.GuiDisable();

        layout.TwoButtons(saveLabel, copyLabel, out bool saveClicked, out bool copyClicked);

        if (_dropdownOpen || _saveModalOpen)
            RayGui.GuiEnable();

        if (!_dropdownOpen && !_saveModalOpen)
        {
            if (saveClicked)
            {
                editScope.CancelAll();
                OpenSavePrompt(view, formula);
            }

            if (copyClicked)
            {
                CopyCliCommand(view, formula, coloring, rendering.UseGpu, rendering.Ssaa);
            }
        }
    }

    public bool DrawOverlay(
        int screenWidth,
        int screenHeight,
        int canvasWidth,
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring,
        TextEditScope editScope)
    {
        bool openedThisFrame = _openedThisFrame;
        _openedThisFrame = false;

        if (_saveModalOpen)
        {
            DrawSaveModal(screenWidth, screenHeight, navigation.State, formula.State, formula.AutoIterations, coloring.State, openedThisFrame);
            return false;
        }

        if (_dropdownOpen)
            return DrawDropdownPopup(screenWidth, screenHeight, navigation, formula, coloring, editScope, openedThisFrame);

        DrawCycleToast(canvasWidth);
        return false;
    }

    void DrawCycleToast(int canvasWidth)
    {
        if (_cycleToastText.Length == 0 || canvasWidth < 120)
            return;

        double remaining = _cycleToastUntil - Raylib.GetTime();
        if (remaining <= 0.0)
            return;

        float alpha = remaining < 0.4 ? (float)(remaining / 0.4) : 1f;
        const int fontSize = 18;
        int textW = Raylib.MeasureText(_cycleToastText, fontSize);
        int pillW = Math.Min(canvasWidth - 32, textW + 32);
        const int pillH = 36;
        int pillX = (canvasWidth - pillW) / 2;
        const int pillY = 16;

        string shown = FitText(_cycleToastText, pillW - 24, fontSize);
        int shownW = Raylib.MeasureText(shown, fontSize);

        var bg = new Color(16, 20, 30, (int)(225 * alpha));
        var border = new Color(96, 176, 232, (int)(235 * alpha));
        var textCol = new Color(236, 240, 248, (int)(255 * alpha));

        var rect = new Rectangle { x = pillX, y = pillY, width = pillW, height = pillH };
        Raylib.DrawRectangleRec(rect, bg);
        Raylib.DrawRectangleLinesEx(rect, 1f, border);
        Raylib.DrawText(shown, pillX + (pillW - shownW) / 2, pillY + 9, fontSize, textCol);
    }

    bool DrawDropdownPopup(
        int screenWidth,
        int screenHeight,
        NavigationFeature navigation,
        FormulaFeature formula,
        ColoringFeature coloring,
        TextEditScope editScope,
        bool openedThisFrame)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
        {
            _dropdownOpen = false;
            return false;
        }

        // Build visual rows (category headers + bookmark items).
        var rows = new List<(bool IsHeader, string Label, int EntryIndex)>(_entries.Count + 8);
        string? lastCategory = null;
        for (int i = 0; i < _entries.Count; i++)
        {
            string category = string.IsNullOrWhiteSpace(_entries[i].Category)
                ? (_entries[i].Curated ? "Curated" : "Saved")
                : _entries[i].Category;

            if (!string.Equals(category, lastCategory, StringComparison.Ordinal))
            {
                rows.Add((true, category, -1));
                lastCategory = category;
            }

            rows.Add((false, _entries[i].DisplayName, i));
        }

        float contentHeight = 8f;
        for (int i = 0; i < rows.Count; i++)
            contentHeight += rows[i].IsHeader ? HeaderHeight : RowHeight;

        float popupWidth = Math.Max(_triggerRect.width, 300f);
        float popupX = Math.Clamp(_triggerRect.x + _triggerRect.width - popupWidth, 8f, Math.Max(8f, screenWidth - popupWidth - 8f));
        float spaceBelow = screenHeight - (_triggerRect.y + _triggerRect.height) - 10f;
        float spaceAbove = _triggerRect.y - 10f;
        float popupHeight = Math.Min(contentHeight, MaxPopupHeight);
        float popupY;
        if (spaceBelow >= popupHeight || spaceBelow >= spaceAbove)
        {
            popupHeight = Math.Min(popupHeight, Math.Max(120f, spaceBelow));
            popupY = _triggerRect.y + _triggerRect.height + 2f;
        }
        else
        {
            popupHeight = Math.Min(popupHeight, Math.Max(120f, spaceAbove));
            popupY = _triggerRect.y - popupHeight - 2f;
        }

        var popup = new Rectangle { x = popupX, y = popupY, width = popupWidth, height = popupHeight };
        Vector2 mouse = Raylib.GetMousePosition();
        bool overPopup = Raylib.CheckCollisionPointRec(mouse, popup);
        bool overTrigger = Raylib.CheckCollisionPointRec(mouse, _triggerRect);

        float maxScroll = Math.Max(0f, contentHeight - popupHeight);
        if (overPopup)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (Math.Abs(wheel) > 0.001f)
                _dropdownScroll = Math.Clamp(_dropdownScroll - wheel * 42f, 0f, maxScroll);
        }
        else
        {
            _dropdownScroll = Math.Clamp(_dropdownScroll, 0f, maxScroll);
        }

        // Draw shadow + popup container.
        Raylib.DrawRectangle((int)popup.x + 4, (int)popup.y + 4, (int)popup.width, (int)popup.height, new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRec(popup, new Color(22, 25, 35, 252));
        Raylib.DrawRectangleLinesEx(popup, 1f, new Color(96, 176, 232, 255));

        bool hasScrollbar = maxScroll > 0.5f;
        float innerWidth = popup.width - (hasScrollbar ? 12f : 4f);
        bool leftClick = !openedThisFrame && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT);

        int clickedEntry = -1;
        int deleteEntry = -1;

        Raylib.BeginScissorMode((int)popup.x + 1, (int)popup.y + 1, (int)popup.width - 2, (int)popup.height - 2);
        float y = popup.y + 4f - _dropdownScroll;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            float h = row.IsHeader ? HeaderHeight : RowHeight;
            if (y + h >= popup.y && y <= popup.y + popup.height)
            {
                if (row.IsHeader)
                {
                    Raylib.DrawText(row.Label.ToUpperInvariant(), (int)popup.x + 10, (int)y + 4, 12, new Color(128, 152, 186, 255));
                }
                else
                {
                    BookmarkEntry entry = _entries[row.EntryIndex];
                    var rowRect = new Rectangle { x = popup.x + 3f, y = y, width = innerWidth - 2f, height = h };
                    bool rowHovered = overPopup && Raylib.CheckCollisionPointRec(mouse, rowRect);
                    bool selected = row.EntryIndex == _selectedIndex;

                    if (selected)
                    {
                        Raylib.DrawRectangleRec(rowRect, new Color(42, 62, 92, 255));
                        Raylib.DrawRectangle((int)rowRect.x, (int)rowRect.y + 2, 3, (int)h - 4, PanelTheme.CheckFill);
                    }
                    else if (rowHovered)
                    {
                        Raylib.DrawRectangleRec(rowRect, new Color(36, 44, 62, 255));
                    }

                    var deleteRect = new Rectangle
                    {
                        x = rowRect.x + rowRect.width - 22f,
                        y = rowRect.y + 3f,
                        width = 18f,
                        height = h - 6f,
                    };
                    bool showDelete = !entry.Curated;
                    bool deleteHovered = showDelete && overPopup && Raylib.CheckCollisionPointRec(mouse, deleteRect);

                    int textMaxWidth = (int)rowRect.width - (showDelete ? 34 : 18);
                    string fitted = FitText(entry.DisplayName, textMaxWidth, 15);
                    Color textColor = selected || rowHovered ? PanelTheme.TitleText : new Color(214, 218, 228, 255);
                    Raylib.DrawText(fitted, (int)rowRect.x + 10, (int)rowRect.y + 5, 15, textColor);

                    if (showDelete)
                    {
                        if (deleteHovered)
                            Raylib.DrawRectangleRec(deleteRect, new Color(150, 48, 56, 220));
                        Raylib.DrawText("x", (int)deleteRect.x + 5, (int)deleteRect.y + 2, 14, deleteHovered ? PanelTheme.TitleText : PanelTheme.MutedText);
                    }

                    if (leftClick && rowHovered)
                    {
                        if (deleteHovered)
                            deleteEntry = row.EntryIndex;
                        else
                            clickedEntry = row.EntryIndex;
                    }
                }
            }

            y += h;
        }
        Raylib.EndScissorMode();

        if (hasScrollbar)
        {
            float trackHeight = popup.height - 8f;
            float thumbHeight = Math.Max(26f, trackHeight * (popupHeight / contentHeight));
            float thumbY = popup.y + 4f + (trackHeight - thumbHeight) * (_dropdownScroll / maxScroll);
            Raylib.DrawRectangle((int)(popup.x + popup.width - 7f), (int)popup.y + 4, 4, (int)trackHeight, new Color(34, 38, 50, 255));
            Raylib.DrawRectangle((int)(popup.x + popup.width - 7f), (int)thumbY, 4, (int)thumbHeight, new Color(102, 136, 178, 255));
        }

        if (deleteEntry >= 0)
        {
            DeleteAt(deleteEntry);
            return false;
        }

        if (clickedEntry >= 0)
        {
            editScope.CancelAll();
            _dropdownOpen = false;
            return ApplyByIndex(clickedEntry, navigation, formula, coloring);
        }

        if (!openedThisFrame
            && (Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT) || Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_RIGHT))
            && !overPopup
            && !overTrigger)
        {
            _dropdownOpen = false;
        }

        return false;
    }

    void DrawSaveModal(
        int screenWidth,
        int screenHeight,
        ViewportState view,
        FormulaParams formula,
        bool autoIterations,
        ColoringParams coloring,
        bool openedThisFrame)
    {
        // Dim backdrop.
        Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(4, 6, 12, 165));

        const float cardW = 480f;
        const float cardH = 196f;
        var card = new Rectangle
        {
            x = MathF.Round((screenWidth - cardW) * 0.5f),
            y = MathF.Round((screenHeight - cardH) * 0.5f),
            width = cardW,
            height = cardH,
        };

        Raylib.DrawRectangle((int)card.x + 6, (int)card.y + 6, (int)card.width, (int)card.height, new Color(0, 0, 0, 140));
        Raylib.DrawRectangleRec(card, new Color(24, 28, 38, 255));
        Raylib.DrawRectangleLinesEx(card, 1.5f, new Color(96, 176, 232, 255));

        int padX = (int)card.x + 20;
        int curY = (int)card.y + 18;
        Raylib.DrawText("Save Bookmark", padX, curY, 22, PanelTheme.TitleText);
        curY += 30;
        Raylib.DrawText("Name this spot (leave blank for automatic coordinates/zoom label):", padX, curY, 13, PanelTheme.MutedText);
        curY += 24;

        var inputRect = new Rectangle { x = padX, y = curY, width = card.width - 40f, height = 34f };
        Raylib.DrawRectangleRec(inputRect, new Color(16, 19, 28, 255));
        Raylib.DrawRectangleLinesEx(inputRect, 1.5f, PanelTheme.CheckFill);

        // Process keyboard input into the modal text buffer.
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_CONTROL) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_CONTROL);
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.KEY_V))
        {
            string? clip = Raylib.GetClipboardTextAsString();
            if (!string.IsNullOrEmpty(clip))
            {
                foreach (char ch in clip)
                {
                    if (_nameBuffer.Length >= MaxNameLength) break;
                    if (ch >= 32 && ch <= 126)
                        _nameBuffer += ch;
                }
            }
        }

        int codepoint = Raylib.GetCharPressed();
        while (codepoint > 0)
        {
            if (codepoint is >= 32 and <= 126 && _nameBuffer.Length < MaxNameLength)
                _nameBuffer += (char)codepoint;
            codepoint = Raylib.GetCharPressed();
        }

        if ((Raylib.IsKeyPressed(KeyboardKey.KEY_BACKSPACE) || Raylib.IsKeyPressedRepeat(KeyboardKey.KEY_BACKSPACE))
            && _nameBuffer.Length > 0)
        {
            _nameBuffer = _nameBuffer[..^1];
        }

        if (_nameBuffer.Length == 0)
        {
            string placeholder = FitText(_fallbackName, (int)inputRect.width - 20, 16);
            Raylib.DrawText(placeholder, (int)inputRect.x + 10, (int)inputRect.y + 9, 16, new Color(102, 110, 128, 255));
        }
        else
        {
            string shown = FitTextFromEnd(_nameBuffer, (int)inputRect.width - 24, 16);
            Raylib.DrawText(shown, (int)inputRect.x + 10, (int)inputRect.y + 9, 16, PanelTheme.TitleText);
            if (((int)(Raylib.GetTime() * 2.0) & 1) == 0)
            {
                int caretX = (int)inputRect.x + 10 + Raylib.MeasureText(shown, 16) + 1;
                Raylib.DrawRectangle(caretX, (int)inputRect.y + 7, 2, 20, PanelTheme.CheckFill);
            }
        }

        if (_nameBuffer.Length == 0 && ((int)(Raylib.GetTime() * 2.0) & 1) == 0)
            Raylib.DrawRectangle((int)inputRect.x + 9, (int)inputRect.y + 7, 2, 20, PanelTheme.CheckFill);

        curY += 44;
        string summary = FitText(_modalSummary, (int)card.width - 40, 13);
        Raylib.DrawText(summary, padX, curY, 13, PanelTheme.HudSecondaryText);

        curY += 28;
        float btnW = 112f;
        var cancelBtn = new Rectangle { x = card.x + card.width - 20f - btnW * 2f - 10f, y = curY, width = btnW, height = 32f };
        var saveBtn = new Rectangle { x = card.x + card.width - 20f - btnW, y = curY, width = btnW, height = 32f };

        bool cancelClicked = RayGui.GuiButton(cancelBtn, "Cancel") != 0;
        bool saveClicked = RayGui.GuiButton(saveBtn, "Save") != 0;

        bool confirmKey = !openedThisFrame && (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER) || Raylib.IsKeyPressed(KeyboardKey.KEY_KP_ENTER));
        bool cancelKey = Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE);
        bool clickedOutside = !openedThisFrame
            && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)
            && !Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), card);

        if (saveClicked || confirmKey)
        {
            SaveCurrentSpot(_nameBuffer, view, formula, autoIterations, coloring);
            _savedFeedbackUntil = Raylib.GetTime() + 2.0;
            _saveModalOpen = false;
        }
        else if (cancelClicked || cancelKey || clickedOutside)
        {
            _saveModalOpen = false;
        }
    }

    static string FitText(string text, int maxWidth, int fontSize)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 16)
            return "";
        if (Raylib.MeasureText(text, fontSize) <= maxWidth)
            return text;

        const string ellipsis = "...";
        int end = text.Length;
        while (end > 1 && Raylib.MeasureText(text[..end] + ellipsis, fontSize) > maxWidth)
            end--;
        return text[..end] + ellipsis;
    }

    static string FitTextFromEnd(string text, int maxWidth, int fontSize)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 16)
            return "";
        if (Raylib.MeasureText(text, fontSize) <= maxWidth)
            return text;

        int start = 1;
        while (start < text.Length && Raylib.MeasureText(text[start..], fontSize) > maxWidth)
            start++;
        return text[start..];
    }
}
