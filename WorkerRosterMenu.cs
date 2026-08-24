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
    private const int HeaderHeight = 176;
    private const int FooterHeight = 80;
    private const int RowHeight = 108;
    private const int ButtonWidth = 168;
    private const int ButtonHeight = 52;

    private readonly IReadOnlyList<WorkerRosterEntry> Entries;
    private readonly ITranslationHelper Translation;
    private readonly Action<WorkerRosterEntry, int> OpenContractPreview;
    private readonly ClickableComponent PreviousButton;
    private readonly ClickableComponent NextButton;
    private readonly int PageSize;
    private int CurrentPage;

    public WorkerRosterMenu(
        IReadOnlyList<WorkerRosterEntry> entries,
        ITranslationHelper translation,
        Action<WorkerRosterEntry, int> openContractPreview,
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
        this.PageSize = Math.Max(1, (this.height - HeaderHeight - FooterHeight) / RowHeight);
        this.CurrentPage = Math.Clamp(initialPage, 0, this.PageCount - 1);

        int buttonY = this.yPositionOnScreen + this.height - FooterHeight + 10;
        this.PreviousButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + HorizontalPadding, buttonY, ButtonWidth, ButtonHeight),
            translation.Get("roster.previous"));
        this.NextButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width - HorizontalPadding - ButtonWidth, buttonY, ButtonWidth, ButtonHeight),
            translation.Get("roster.next"));

        this.PreviousButton.myID = 100;
        this.PreviousButton.rightNeighborID = 101;
        this.NextButton.myID = 101;
        this.NextButton.leftNeighborID = 100;

        this.allClickableComponents = new List<ClickableComponent>
        {
            this.PreviousButton,
            this.NextButton
        };

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
        int contentY = this.yPositionOnScreen + 42;

        batch.DrawString(
            Game1.dialogueFont,
            this.Translation.Get("roster.title"),
            new Vector2(contentX, contentY),
            Game1.textColor);

        contentY += 58;
        string subtitle = Game1.parseText(
            this.Translation.Get("roster.subtitle"),
            Game1.smallFont,
            contentWidth);
        batch.DrawString(Game1.smallFont, subtitle, new Vector2(contentX, contentY), Game1.textColor);

        contentY = this.yPositionOnScreen + 140;
        batch.DrawString(
            Game1.smallFont,
            this.Translation.Get("roster.summary", new { total = this.Entries.Count }),
            new Vector2(contentX, contentY),
            Color.DimGray);

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
            Game1.textColor);

        this.DrawButton(batch, this.PreviousButton, this.CurrentPage > 0);
        this.DrawButton(batch, this.NextButton, this.CurrentPage < this.PageCount - 1);

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
            new Rectangle(x + 16, y + 18, 64, 64),
            new Rectangle(0, 0, portraitSize, portraitSize),
            Color.White);

        int textX = x + 96;
        int textWidth = width - 112;
        batch.DrawString(Game1.smallFont, entry.DisplayName, new Vector2(textX, y + 18), Game1.textColor);

        string stateText = this.GetStateText(entry.Availability.State);
        Color stateColor = this.GetStateColor(entry.Availability.State);
        Vector2 stateSize = Game1.smallFont.MeasureString(stateText);
        batch.DrawString(
            Game1.smallFont,
            stateText,
            new Vector2(x + width - stateSize.X - 18, y + 18),
            stateColor);

        string reasonText = Game1.parseText(
            this.GetReasonText(entry.Availability.Reason),
            Game1.smallFont,
            textWidth);
        batch.DrawString(Game1.smallFont, reasonText, new Vector2(textX, y + 54), Color.DimGray);
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

    private string GetStateText(WorkerAvailabilityState state)
    {
        string key = state switch
        {
            WorkerAvailabilityState.EligibleForPreview => "roster.state.eligible",
            WorkerAvailabilityState.TemporarilyUnavailable => "roster.state.unavailable",
            WorkerAvailabilityState.Ineligible => "roster.state.ineligible",
            _ => "roster.state.unknown"
        };

        return this.Translation.Get(key);
    }

    private string GetReasonText(WorkerAvailabilityReason reason)
    {
        string key = reason switch
        {
            WorkerAvailabilityReason.AvailableForPreview => "roster.reason.available",
            WorkerAvailabilityReason.Child => "roster.reason.child",
            WorkerAvailabilityReason.UnsupportedCharacter => "roster.reason.unsupported-character",
            WorkerAvailabilityReason.ActiveFestival => "roster.reason.festival",
            WorkerAvailabilityReason.ActiveEvent => "roster.reason.event",
            WorkerAvailabilityReason.MissingLocation => "roster.reason.missing-location",
            WorkerAvailabilityReason.Sleeping => "roster.reason.sleeping",
            WorkerAvailabilityReason.IslandActivity => "roster.reason.island",
            WorkerAvailabilityReason.MedicalActivity => "roster.reason.medical",
            WorkerAvailabilityReason.WorkActivity => "roster.reason.work",
            WorkerAvailabilityReason.ControlledActivity => "roster.reason.controlled",
            WorkerAvailabilityReason.MovementActivity => "roster.reason.movement",
            WorkerAvailabilityReason.DialogueActivity => "roster.reason.dialogue",
            WorkerAvailabilityReason.ScriptedAnimation => "roster.reason.animation",
            WorkerAvailabilityReason.UnsupportedCustomNpc => "roster.reason.custom-npc",
            _ => "roster.reason.evaluation-failed"
        };

        return this.Translation.Get(key);
    }

    private Color GetStateColor(WorkerAvailabilityState state)
    {
        return state switch
        {
            WorkerAvailabilityState.EligibleForPreview => new Color(35, 110, 45),
            WorkerAvailabilityState.TemporarilyUnavailable => new Color(170, 100, 20),
            WorkerAvailabilityState.Ineligible => new Color(150, 45, 40),
            _ => Color.DimGray
        };
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
