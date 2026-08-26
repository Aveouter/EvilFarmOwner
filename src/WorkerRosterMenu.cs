using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class WorkerRosterMenu : IClickableMenu
{
    private const int MaximumWidth = 860;
    private const int MaximumHeight = 720;
    private const int ScreenMargin = 64;
    private const int HorizontalPadding = 56;
    private const int HeaderHeight = 92;
    private const int SingleFooterHeight = 64;
    private const int MultiFooterHeight = 116;
    private const int RowHeight = 88;
    private const int ButtonWidth = 144;
    private const int ButtonHeight = 44;

    private readonly IReadOnlyList<WorkerRosterEntry> Entries;
    private readonly ITranslationHelper Translation;
    private readonly Action<WorkerRosterEntry, int>? OpenContractPreview;
    private readonly Action<IReadOnlyList<WorkerRosterEntry>, int>? OpenGroupPreview;
    private readonly Action? OpenRecurringContracts;
    private readonly ClickableComponent PreviousButton;
    private readonly ClickableComponent NextButton;
    private readonly ClickableComponent RecurringButton;
    private readonly ClickableComponent AutoSelectButton;
    private readonly ClickableComponent ReviewButton;
    private readonly List<ClickableComponent> RowButtons = new();
    private readonly HashSet<string> SelectedWorkerNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly int PageSize;
    private readonly int FooterHeight;
    private readonly int MaximumSelections;
    private int CurrentPage;

    public WorkerRosterMenu(
        IReadOnlyList<WorkerRosterEntry> entries,
        ITranslationHelper translation,
        Action<WorkerRosterEntry, int> openContractPreview,
        Action? openRecurringContracts = null,
        int initialPage = 0)
        : this(
            entries,
            translation,
            openContractPreview,
            openGroupPreview: null,
            openRecurringContracts,
            maximumSelections: 1,
            initialPage,
            initialSelections: null)
    {
    }

    public WorkerRosterMenu(
        IReadOnlyList<WorkerRosterEntry> entries,
        ITranslationHelper translation,
        Action<IReadOnlyList<WorkerRosterEntry>, int> openGroupPreview,
        int maximumSelections,
        Action? openRecurringContracts = null,
        int initialPage = 0,
        IEnumerable<string>? initialSelections = null)
        : this(
            entries,
            translation,
            openContractPreview: null,
            openGroupPreview,
            openRecurringContracts,
            maximumSelections,
            initialPage,
            initialSelections)
    {
    }

    private WorkerRosterMenu(
        IReadOnlyList<WorkerRosterEntry> entries,
        ITranslationHelper translation,
        Action<WorkerRosterEntry, int>? openContractPreview,
        Action<IReadOnlyList<WorkerRosterEntry>, int>? openGroupPreview,
        Action? openRecurringContracts,
        int maximumSelections,
        int initialPage,
        IEnumerable<string>? initialSelections)
        : base(
            GetMenuX(),
            GetMenuY(),
            GetMenuWidth(),
            GetMenuHeight(),
            showUpperRightCloseButton: true)
    {
        this.Entries = entries;
        this.Translation = translation;
        this.OpenContractPreview = openContractPreview;
        this.OpenGroupPreview = openGroupPreview;
        this.OpenRecurringContracts = openRecurringContracts;
        this.MaximumSelections = openGroupPreview is null
            ? 1
            : ContractSettingsPolicy.NormalizeMaximumConcurrentWorkers(maximumSelections);
        this.FooterHeight = openGroupPreview is null ? SingleFooterHeight : MultiFooterHeight;
        if (initialSelections is not null)
        {
            HashSet<string> validNames = entries.Select(entry => entry.InternalName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string name in initialSelections.Where(validNames.Contains).Take(this.MaximumSelections))
                this.SelectedWorkerNames.Add(name);
        }
        this.PageSize = Math.Max(1, (this.height - HeaderHeight - FooterHeight) / RowHeight);
        this.CurrentPage = Math.Clamp(initialPage, 0, this.PageCount - 1);

        int buttonY = this.yPositionOnScreen + this.height - FooterHeight + 8;
        this.PreviousButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + HorizontalPadding, buttonY, ButtonWidth, ButtonHeight),
            translation.Get("roster.previous"));
        this.NextButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width - HorizontalPadding - ButtonWidth, buttonY, ButtonWidth, ButtonHeight),
            translation.Get("roster.next"));
        this.RecurringButton = new ClickableComponent(
            new Rectangle(
                this.xPositionOnScreen + this.width / 2 - 110,
                buttonY,
                220,
                ButtonHeight),
            translation.Get("roster.recurring"));
        int groupButtonY = this.yPositionOnScreen + this.height - this.FooterHeight + 8;
        this.AutoSelectButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width / 2 - 230, groupButtonY, 220, ButtonHeight),
            translation.Get("roster.auto-select"));
        this.ReviewButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width / 2 + 10, groupButtonY, 220, ButtonHeight),
            translation.Get("roster.review-selection"));

        this.PreviousButton.myID = 100;
        this.NextButton.myID = 101;
        this.RecurringButton.myID = 102;
        this.AutoSelectButton.myID = 103;
        this.ReviewButton.myID = 104;
        this.RebuildClickableComponents();
        this.snapToDefaultClickableComponent();
    }

    private int PageCount => Math.Max(1, (this.Entries.Count + this.PageSize - 1) / this.PageSize);

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        int selectedRow = this.RowButtons.FindIndex(button => button.containsPoint(x, y));
        if (selectedRow >= 0)
        {
            Game1.playSound("smallSelect");
            WorkerRosterEntry entry = this.GetEntryForRow(selectedRow);
            if (this.OpenGroupPreview is null)
                this.OpenContractPreview?.Invoke(entry, this.CurrentPage);
            else
                this.ToggleSelection(entry);
            return;
        }

        if (this.OpenGroupPreview is not null && this.AutoSelectButton.containsPoint(x, y))
        {
            this.AutoSelect();
            return;
        }

        if (this.OpenGroupPreview is not null
            && this.SelectedWorkerNames.Count > 0
            && this.ReviewButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            this.OpenGroupPreview(this.GetSelectedEntries(), this.CurrentPage);
            return;
        }

        if (this.PreviousButton.containsPoint(x, y) && this.CurrentPage > 0)
        {
            this.ChangePage(-1);
            return;
        }

        if (this.NextButton.containsPoint(x, y) && this.CurrentPage < this.PageCount - 1)
        {
            this.ChangePage(1);
            return;
        }

        if (this.OpenRecurringContracts is not null && this.RecurringButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            this.OpenRecurringContracts();
            return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    public override void receiveScrollWheelAction(int direction)
    {
        if (direction > 0 && this.CurrentPage > 0)
            this.ChangePage(-1);
        else if (direction < 0 && this.CurrentPage < this.PageCount - 1)
            this.ChangePage(1);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.PageUp && this.CurrentPage > 0)
        {
            this.ChangePage(-1);
            return;
        }

        if (key == Keys.PageDown && this.CurrentPage < this.PageCount - 1)
        {
            this.ChangePage(1);
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void snapToDefaultClickableComponent()
    {
        this.currentlySnappedComponent = this.RowButtons.Count > 0
            ? this.RowButtons[0]
            : this.OpenRecurringContracts is not null
                ? this.RecurringButton
                : this.upperRightCloseButton;
        this.snapCursorToCurrentSnappedComponent();
    }

    public override void draw(SpriteBatch batch)
    {
        Game1.drawDialogueBox(
            this.xPositionOnScreen,
            this.yPositionOnScreen,
            this.width,
            this.height,
            speaker: false,
            drawOnlyBox: true);

        int contentX = this.xPositionOnScreen + HorizontalPadding;
        int contentWidth = this.width - HorizontalPadding * 2;
        int contentY = this.yPositionOnScreen + 40;

        batch.DrawString(
            Game1.dialogueFont,
            this.Translation.Get("roster.title"),
            new Vector2(contentX, contentY),
            Game1.textColor);

        if (this.OpenGroupPreview is not null)
        {
            string selection = this.Translation.Get("roster.selection-summary", new
            {
                selected = this.SelectedWorkerNames.Count,
                maximum = this.MaximumSelections
            });
            Vector2 selectionSize = Game1.smallFont.MeasureString(selection);
            batch.DrawString(
                Game1.smallFont,
                selection,
                new Vector2(contentX + contentWidth - selectionSize.X, contentY + 12),
                Color.DimGray);
        }

        int rowY = this.yPositionOnScreen + HeaderHeight;
        if (this.Entries.Count == 0)
        {
            batch.DrawString(
                Game1.smallFont,
                this.Translation.Get("roster.empty"),
                new Vector2(contentX + 16, rowY + 20),
                Color.DimGray);
        }
        else
        {
            int rowIndex = 0;
            foreach (WorkerRosterEntry entry in this.Entries.Skip(this.CurrentPage * this.PageSize).Take(this.PageSize))
            {
                ClickableComponent rowButton = this.RowButtons[rowIndex++];
                bool selected = rowButton == this.currentlySnappedComponent
                    || rowButton.containsPoint(Game1.getMouseX(), Game1.getMouseY());
                this.DrawRow(
                    batch,
                    entry,
                    contentX,
                    rowY,
                    contentWidth,
                    selected,
                    this.SelectedWorkerNames.Contains(entry.InternalName));
                rowY += RowHeight;
            }
        }

        if (this.PageCount > 1)
        {
            string pageText = this.Translation.Get("roster.page", new
            {
                current = this.CurrentPage + 1,
                total = this.PageCount
            });
            Vector2 pageSize = Game1.smallFont.MeasureString(pageText);
            batch.DrawString(
                Game1.smallFont,
                pageText,
                new Vector2(
                    this.xPositionOnScreen + this.width / 2f - pageSize.X / 2f,
                    this.PreviousButton.bounds.Center.Y - pageSize.Y / 2f),
                Color.DimGray);
        }

        if (this.CurrentPage > 0)
            this.DrawButton(batch, this.PreviousButton);
        if (this.CurrentPage < this.PageCount - 1)
            this.DrawButton(batch, this.NextButton);
        if (this.OpenRecurringContracts is not null)
            this.DrawButton(batch, this.RecurringButton);
        if (this.OpenGroupPreview is not null)
        {
            this.DrawButton(batch, this.AutoSelectButton);
            this.DrawButton(batch, this.ReviewButton, this.SelectedWorkerNames.Count > 0);
        }

        base.draw(batch);
        this.drawMouse(batch);
    }

    private void DrawRow(
        SpriteBatch batch,
        WorkerRosterEntry entry,
        int x,
        int y,
        int width,
        bool hovered,
        bool checkedWorker)
    {
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            x,
            y + 4,
            width,
            RowHeight - 8,
            checkedWorker
                ? new Color(225, 245, 205)
                : hovered ? new Color(255, 245, 190) : Color.White,
            0.8f,
            drawShadow: false);

        int portraitSize = Math.Min(64, Math.Min(entry.Portrait.Width, entry.Portrait.Height));
        batch.Draw(
            entry.Portrait,
            new Rectangle(x + 16, y + 18, 56, 56),
            new Rectangle(0, 0, portraitSize, portraitSize),
            Color.White);

        int textX = x + 88;
        batch.DrawString(Game1.smallFont, entry.DisplayName, new Vector2(textX, y + 14), Game1.textColor);

        string friendship = this.Translation.Get("roster.worker.friendship", new
        {
            hearts = entry.WagePreview.FriendshipHearts
        });
        batch.DrawString(Game1.smallFont, friendship, new Vector2(textX, y + 48), Color.DimGray);

        string wage = this.Translation.Get("roster.worker.wage", new
        {
            gold = entry.WagePreview.MaximumAuthorizedWage
        });
        Vector2 wageSize = Game1.smallFont.MeasureString(wage);
        batch.DrawString(Game1.smallFont, wage, new Vector2(x + width - wageSize.X - 20, y + 31), new Color(35, 110, 45));
        if (this.OpenGroupPreview is not null)
        {
            string marker = checkedWorker ? "✓" : "";
            batch.DrawString(
                Game1.dialogueFont,
                marker,
                new Vector2(x + 4, y + 20),
                new Color(35, 110, 45));
        }
    }

    private void DrawButton(SpriteBatch batch, ClickableComponent button)
    {
        this.DrawButton(batch, button, enabled: true);
    }

    private void DrawButton(SpriteBatch batch, ClickableComponent button, bool enabled)
    {
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            button.bounds.X,
            button.bounds.Y,
            button.bounds.Width,
            button.bounds.Height,
            enabled ? Color.White : Color.Gray,
            0.8f,
            drawShadow: false);

        Vector2 labelSize = Game1.smallFont.MeasureString(button.name);
        batch.DrawString(
            Game1.smallFont,
            button.name,
            new Vector2(
                button.bounds.Center.X - labelSize.X / 2,
                button.bounds.Center.Y - labelSize.Y / 2),
            enabled ? Game1.textColor : Color.DimGray);
    }

    private void ToggleSelection(WorkerRosterEntry entry)
    {
        if (this.SelectedWorkerNames.Remove(entry.InternalName))
            return;
        if (this.SelectedWorkerNames.Count >= this.MaximumSelections)
        {
            Game1.playSound("cancel");
            return;
        }
        this.SelectedWorkerNames.Add(entry.InternalName);
    }

    private void AutoSelect()
    {
        IReadOnlyList<ConcurrentWorkerCandidate> selected = ConcurrentWorkerSelectionPolicy.Select(
            this.Entries.Select(entry => new ConcurrentWorkerCandidate(
                entry.InternalName,
                entry.WagePreview.EfficiencyMultiplier,
                entry.WagePreview.MaximumAuthorizedWage,
                entry.WagePreview.FriendshipHearts)),
            this.MaximumSelections,
            Game1.player.Money);
        this.SelectedWorkerNames.Clear();
        foreach (ConcurrentWorkerCandidate candidate in selected)
            this.SelectedWorkerNames.Add(candidate.WorkerName);
        Game1.playSound(selected.Count > 0 ? "shwip" : "cancel");
    }

    private IReadOnlyList<WorkerRosterEntry> GetSelectedEntries()
    {
        return this.Entries
            .Where(entry => this.SelectedWorkerNames.Contains(entry.InternalName))
            .OrderBy(entry => entry.InternalName, StringComparer.Ordinal)
            .ToArray();
    }

    private void ChangePage(int offset)
    {
        this.CurrentPage = Math.Clamp(this.CurrentPage + offset, 0, this.PageCount - 1);
        Game1.playSound("shwip");
        this.RebuildClickableComponents();
        this.snapToDefaultClickableComponent();
    }

    private WorkerRosterEntry GetEntryForRow(int row)
    {
        return this.Entries[this.CurrentPage * this.PageSize + row];
    }

    private void RebuildClickableComponents()
    {
        this.RowButtons.Clear();
        int visibleRows = Math.Min(this.PageSize, this.Entries.Count - this.CurrentPage * this.PageSize);
        int footerFocusId = this.OpenRecurringContracts is not null
            ? 102
            : this.CurrentPage > 0
                ? 100
                : this.CurrentPage < this.PageCount - 1
                    ? 101
                    : -1;
        int contentX = this.xPositionOnScreen + HorizontalPadding;
        int contentWidth = this.width - HorizontalPadding * 2;
        for (int row = 0; row < visibleRows; row++)
        {
            this.RowButtons.Add(new ClickableComponent(
                new Rectangle(contentX, this.yPositionOnScreen + HeaderHeight + row * RowHeight + 4, contentWidth, RowHeight - 8),
                this.GetEntryForRow(row).DisplayName)
            {
                myID = row,
                upNeighborID = row == 0 ? -1 : row - 1,
                downNeighborID = row == visibleRows - 1
                    ? footerFocusId
                    : row + 1
            });
        }

        int lastRowId = this.RowButtons.Count > 0 ? this.RowButtons[^1].myID : -1;
        this.PreviousButton.leftNeighborID = -1;
        this.PreviousButton.rightNeighborID = this.OpenRecurringContracts is not null
            ? 102
            : this.CurrentPage < this.PageCount - 1 ? 101 : -1;
        this.PreviousButton.upNeighborID = lastRowId;
        this.NextButton.leftNeighborID = this.OpenRecurringContracts is not null
            ? 102
            : this.CurrentPage > 0 ? 100 : -1;
        this.NextButton.rightNeighborID = -1;
        this.NextButton.upNeighborID = lastRowId;
        this.RecurringButton.leftNeighborID = this.CurrentPage > 0 ? 100 : -1;
        this.RecurringButton.rightNeighborID = this.CurrentPage < this.PageCount - 1 ? 101 : -1;
        this.RecurringButton.upNeighborID = lastRowId;

        this.allClickableComponents = new List<ClickableComponent>(this.RowButtons);
        if (this.CurrentPage > 0)
            this.allClickableComponents.Add(this.PreviousButton);
        if (this.CurrentPage < this.PageCount - 1)
            this.allClickableComponents.Add(this.NextButton);
        if (this.OpenRecurringContracts is not null)
            this.allClickableComponents.Add(this.RecurringButton);
        if (this.OpenGroupPreview is not null)
        {
            this.allClickableComponents.Add(this.AutoSelectButton);
            this.allClickableComponents.Add(this.ReviewButton);
        }
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);
    }

    private static int GetMenuWidth()
    {
        return Math.Min(MaximumWidth, Math.Max(640, Game1.uiViewport.Width - ScreenMargin));
    }

    private static int GetMenuHeight()
    {
        return Math.Min(MaximumHeight, Math.Max(560, Game1.uiViewport.Height - ScreenMargin));
    }

    private static int GetMenuX()
    {
        return Game1.uiViewport.Width / 2 - GetMenuWidth() / 2;
    }

    private static int GetMenuY()
    {
        return Game1.uiViewport.Height / 2 - GetMenuHeight() / 2;
    }
}
