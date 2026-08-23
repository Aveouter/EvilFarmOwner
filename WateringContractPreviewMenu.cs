using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class WorkContractPreviewMenu : IClickableMenu
{
    private const int MaximumWidth = 900;
    private const int MaximumHeight = 700;
    private const int ScreenMargin = 64;
    private const int HorizontalPadding = 56;
    private const int BackButtonWidth = 180;
    private const int ConfirmButtonWidth = 300;
    private const int ButtonHeight = 52;

    private readonly WorkerRosterEntry Worker;
    private readonly WorkContractPreview Preview;
    private readonly NamedFarmTask Task;
    private readonly ITranslationHelper Translation;
    private readonly Action ReturnToRoster;
    private readonly Func<bool> ConfirmContract;
    private readonly ClickableComponent BackButton;
    private readonly ClickableComponent ConfirmButton;

    public WorkContractPreviewMenu(
        WorkerRosterEntry worker,
        WorkContractPreview preview,
        NamedFarmTask task,
        ITranslationHelper translation,
        Action returnToRoster,
        Func<bool> confirmContract)
        : base(
            GetMenuX(),
            GetMenuY(),
            GetMenuWidth(),
            GetMenuHeight(),
            showUpperRightCloseButton: true)
    {
        this.Worker = worker;
        this.Preview = preview;
        this.Task = task;
        this.Translation = translation;
        this.ReturnToRoster = returnToRoster;
        this.ConfirmContract = confirmContract;

        this.BackButton = new ClickableComponent(
            new Rectangle(
                this.xPositionOnScreen + HorizontalPadding,
                this.yPositionOnScreen + this.height - ButtonHeight - 28,
                BackButtonWidth,
                ButtonHeight),
            translation.Get("contract.back"));
        this.BackButton.myID = 100;
        this.BackButton.rightNeighborID = 101;

        this.ConfirmButton = new ClickableComponent(
            new Rectangle(
                this.xPositionOnScreen + this.width - HorizontalPadding - ConfirmButtonWidth,
                this.yPositionOnScreen + this.height - ButtonHeight - 28,
                ConfirmButtonWidth,
                ButtonHeight),
            this.GetConfirmText());
        this.ConfirmButton.myID = 101;
        this.ConfirmButton.leftNeighborID = 100;

        this.allClickableComponents = new List<ClickableComponent> { this.BackButton, this.ConfirmButton };
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);

        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.ConfirmButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            if (this.ConfirmContract())
                Game1.activeClickableMenu = null;
            return;
        }

        if (this.BackButton.containsPoint(x, y))
        {
            Game1.playSound("bigDeSelect");
            this.ReturnToRoster();
            return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Back)
        {
            Game1.playSound("bigDeSelect");
            this.ReturnToRoster();
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void snapToDefaultClickableComponent()
    {
        this.currentlySnappedComponent = this.getComponentWithID(this.ConfirmButton.myID);
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
            this.Translation.Get(this.Task == NamedFarmTask.Watering
                ? "contract.title.watering"
                : "contract.title.harvesting"),
            new Vector2(contentX, contentY),
            Game1.textColor);

        contentY += 58;
        string subtitle = Game1.parseText(
            this.Translation.Get(this.Task == NamedFarmTask.Watering
                ? "contract.subtitle.watering"
                : "contract.subtitle.harvesting"),
            Game1.smallFont,
            contentWidth);
        batch.DrawString(Game1.smallFont, subtitle, new Vector2(contentX, contentY), Color.DimGray);

        int workerY = this.yPositionOnScreen + 132;
        this.DrawWorkerCard(batch, contentX, workerY, contentWidth);

        int detailsY = workerY + 112;
        int columnGap = 52;
        int columnWidth = (contentWidth - columnGap) / 2;
        int rightColumnX = contentX + columnWidth + columnGap;

        this.DrawDetailRow(
            batch,
            contentX,
            detailsY,
            columnWidth,
            "contract.task",
            this.Translation.Get(this.Task == NamedFarmTask.Watering
                ? "contract.task.watering"
                : "contract.task.harvesting"));
        this.DrawDetailRow(
            batch,
            contentX,
            detailsY + 42,
            columnWidth,
            "contract.limit",
            this.Translation.Get(this.Task == NamedFarmTask.Watering
                ? "contract.limit.value.watering"
                : "contract.limit.value.harvesting"));
        this.DrawDetailRow(batch, contentX, detailsY + 84, columnWidth, "contract.day", this.GetDayText());
        this.DrawDetailRow(batch, contentX, detailsY + 126, columnWidth, "contract.base-rate", this.Translation.Get("contract.base-rate.value", new { gold = this.Preview.BaseHourlyWage }));
        this.DrawDetailRow(batch, contentX, detailsY + 168, columnWidth, "contract.friendship", this.Translation.Get("contract.friendship.value", new
        {
            hearts = this.Preview.FriendshipHearts,
            band = this.GetFriendshipBandText()
        }));

        this.DrawDetailRow(batch, rightColumnX, detailsY, columnWidth, "contract.friendship-multiplier", FormatMultiplier(this.Preview.FriendshipMultiplier));
        this.DrawDetailRow(batch, rightColumnX, detailsY + 42, columnWidth, "contract.day-multiplier", FormatMultiplier(this.Preview.DayMultiplier));
        this.DrawDetailRow(batch, rightColumnX, detailsY + 84, columnWidth, "contract.efficiency", FormatMultiplier(this.Preview.EfficiencyMultiplier));
        this.DrawDetailRow(batch, rightColumnX, detailsY + 126, columnWidth, "contract.callout", this.Translation.Get("contract.gold", new { gold = this.Preview.MinimumCalloutWage }), highlight: true);
        this.DrawDetailRow(batch, rightColumnX, detailsY + 168, columnWidth, "contract.overtime", this.Translation.Get("contract.overtime.disabled", new
        {
            multiplier = FormatMultiplier(this.Preview.OvertimeMultiplier),
            hours = this.Preview.MaximumOvertimeHours
        }));
        this.DrawDetailRow(batch, rightColumnX, detailsY + 210, columnWidth, "contract.maximum", this.Translation.Get("contract.gold", new { gold = this.Preview.MaximumAuthorizedWage }), highlight: true);

        int noticeY = this.BackButton.bounds.Y - 48;
        string notice = Game1.parseText(
            this.Preview.DayKind == ContractDayKind.RestDay
                ? this.Translation.Get("contract.notice.rest-day")
                : this.Translation.Get("contract.notice.confirm"),
            Game1.smallFont,
            contentWidth);
        batch.DrawString(Game1.smallFont, notice, new Vector2(contentX, noticeY), new Color(150, 45, 40));

        this.DrawButton(batch, this.BackButton);
        this.DrawButton(batch, this.ConfirmButton);
        base.draw(batch);
        this.drawMouse(batch);
    }

    private void DrawWorkerCard(SpriteBatch batch, int x, int y, int width)
    {
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            x,
            y,
            width,
            96,
            Color.White,
            0.8f,
            drawShadow: false);

        int portraitSize = Math.Min(64, Math.Min(this.Worker.Portrait.Width, this.Worker.Portrait.Height));
        batch.Draw(
            this.Worker.Portrait,
            new Rectangle(x + 16, y + 16, 64, 64),
            new Rectangle(0, 0, portraitSize, portraitSize),
            Color.White);

        batch.DrawString(Game1.dialogueFont, this.Worker.DisplayName, new Vector2(x + 100, y + 16), Game1.textColor);
        batch.DrawString(
            Game1.smallFont,
            this.Translation.Get("contract.worker.selected"),
            new Vector2(x + 102, y + 58),
            Color.DimGray);
    }

    private void DrawDetailRow(
        SpriteBatch batch,
        int x,
        int y,
        int width,
        string labelKey,
        string value,
        bool highlight = false)
    {
        string label = this.Translation.Get(labelKey);
        batch.DrawString(Game1.smallFont, label, new Vector2(x, y), Color.DimGray);

        Vector2 valueSize = Game1.smallFont.MeasureString(value);
        batch.DrawString(
            Game1.smallFont,
            value,
            new Vector2(x + width - valueSize.X, y),
            highlight ? new Color(35, 110, 45) : Game1.textColor);
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

    private string GetDayText()
    {
        return this.Preview.DayKind == ContractDayKind.RestDay
            ? this.Translation.Get("contract.day.rest")
            : this.Translation.Get("contract.day.regular");
    }

    private string GetConfirmText()
    {
        if (this.Preview.DayKind == ContractDayKind.RestDay)
        {
            return this.Translation.Get(this.Task == NamedFarmTask.Watering
                ? "contract.confirm.rest-day.watering"
                : "contract.confirm.rest-day.harvesting");
        }

        return this.Translation.Get(this.Task == NamedFarmTask.Watering
            ? "contract.confirm.regular.watering"
            : "contract.confirm.regular.harvesting");
    }

    private string GetFriendshipBandText()
    {
        string key = this.Preview.FriendshipBand switch
        {
            FriendshipWageBand.HighRisk => "contract.friendship-band.high-risk",
            FriendshipWageBand.ElevatedRisk => "contract.friendship-band.elevated-risk",
            FriendshipWageBand.Standard => "contract.friendship-band.standard",
            _ => "contract.friendship-band.trusted"
        };

        return this.Translation.Get(key);
    }

    private static string FormatMultiplier(decimal multiplier)
    {
        return $"{multiplier:0.00}x";
    }

    private static int GetMenuWidth()
    {
        return Math.Min(MaximumWidth, Math.Max(680, Game1.uiViewport.Width - ScreenMargin));
    }

    private static int GetMenuHeight()
    {
        return Math.Min(MaximumHeight, Math.Max(620, Game1.uiViewport.Height - ScreenMargin));
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
