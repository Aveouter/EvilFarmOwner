using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class WorkerRosterMenu : IClickableMenu
{
    private const int MaximumWidth = 960;
    private const int MaximumHeight = 760;
    private const int ScreenMargin = 64;
    private const int HorizontalPadding = 56;
    private const int HeaderHeight = 108;
    private const int FooterHeight = 64;
    private const int RowHeight = 92;
    private const int ButtonWidth = 144;
    private const int ButtonHeight = 44;

    private readonly IReadOnlyList<WorkerRosterEntry> Entries;
    private readonly ITranslationHelper Translation;
    private readonly Action<WorkerRosterEntry, int> OpenContractPreview;
    private readonly Action? OpenRecurringContracts;
    private readonly ClickableComponent PreviousButton;
    private readonly ClickableComponent NextButton;
    private readonly ClickableComponent RecurringButton;
    private readonly int PageSize;
    private int CurrentPage;

    public WorkerRosterMenu(
        IReadOnlyList<WorkerRosterEntry> entries,
        ITranslationHelper translation,
        Action<WorkerRosterEntry, int> openContractPreview,
        Action? openRecurringContracts = null,
        int initialPage = 0)
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
        this.OpenRecurringContracts = openRecurringContracts;
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

        this.PreviousButton.myID = 100;
        this.PreviousButton.rightNeighborID = 101;
        this.NextButton.myID = 101;
        this.NextButton.leftNeighborID = 100;
        this.RecurringButton.myID = 102;

        this.allClickableComponents = new List<ClickableComponent>
        {
            this.PreviousButton,
            this.NextButton
        };
        if (this.OpenRecurringContracts is not null)
            this.allClickableComponents.Add(this.RecurringButton);

        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);

        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    private int PageCount => Math.Max(1, (this.Entries.Count + this.PageSize - 1) / this.PageSize);

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        WorkerRosterEntry? selectedEntry = this.GetEligibleEntryAt(x, y);
        if (selectedEntry is not null)
        {
            Game1.playSound("smallSelect");
            this.OpenContractPreview(selectedEntry, this.CurrentPage);
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
        this.currentlySnappedComponent = this.getComponentWithID(
            this.PageCount > 1 ? this.NextButton.myID : this.PreviousButton.myID);
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
            foreach (WorkerRosterEntry entry in this.Entries.Skip(this.CurrentPage * this.PageSize).Take(this.PageSize))
            {
                this.DrawRow(batch, entry, contentX, rowY, contentWidth);
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

        base.draw(batch);
        this.drawMouse(batch);
    }

    private void DrawRow(SpriteBatch batch, WorkerRosterEntry entry, int x, int y, int width)
    {
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            x,
            y + 4,
            width,
            RowHeight - 8,
            Color.White,
            0.8f,
            drawShadow: false);

        int portraitSize = Math.Min(64, Math.Min(entry.Portrait.Width, entry.Portrait.Height));
        batch.Draw(
            entry.Portrait,
            new Rectangle(x + 16, y + 18, 56, 56),
            new Rectangle(0, 0, portraitSize, portraitSize),
            Color.White);

        int textX = x + 88;
        int textWidth = width - 106;
        batch.DrawString(Game1.smallFont, entry.DisplayName, new Vector2(textX, y + 14), Game1.textColor);

        string employmentText = Game1.parseText(
            this.Translation.Get("roster.worker.employment", new
            {
                hearts = entry.WagePreview.FriendshipHearts,
                hourly = entry.WagePreview.MinimumCalloutWage,
                maximum = entry.WagePreview.MaximumAuthorizedWage
            }),
            Game1.smallFont,
            textWidth);
        batch.DrawString(Game1.smallFont, employmentText, new Vector2(textX, y + 48), Color.DimGray);
    }

    private void DrawButton(SpriteBatch batch, ClickableComponent button)
    {
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            button.bounds.X,
            button.bounds.Y,
            button.bounds.Width,
            button.bounds.Height,
            Color.White,
            0.8f,
            drawShadow: false);

        Vector2 labelSize = Game1.smallFont.MeasureString(button.name);
        batch.DrawString(
            Game1.smallFont,
            button.name,
            new Vector2(
                button.bounds.Center.X - labelSize.X / 2,
                button.bounds.Center.Y - labelSize.Y / 2),
            Game1.textColor);
    }

    private void ChangePage(int offset)
    {
        this.CurrentPage = Math.Clamp(this.CurrentPage + offset, 0, this.PageCount - 1);
        Game1.playSound("shwip");
    }

    private WorkerRosterEntry? GetEligibleEntryAt(int x, int y)
    {
        int contentX = this.xPositionOnScreen + HorizontalPadding;
        int contentWidth = this.width - HorizontalPadding * 2;
        int firstRowY = this.yPositionOnScreen + HeaderHeight;
        Rectangle rowsBounds = new(
            contentX,
            firstRowY,
            contentWidth,
            this.PageSize * RowHeight);

        if (!rowsBounds.Contains(x, y))
            return null;

        int rowOffset = (y - firstRowY) / RowHeight;
        int entryIndex = this.CurrentPage * this.PageSize + rowOffset;
        if (entryIndex < 0 || entryIndex >= this.Entries.Count)
            return null;

        WorkerRosterEntry entry = this.Entries[entryIndex];
        return entry.Availability.State == WorkerAvailabilityState.EligibleForPreview
            ? entry
            : null;
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
