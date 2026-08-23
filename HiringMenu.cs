using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class HiringMenu : IClickableMenu
{
    private const int MenuWidth = 760;
    private const int MenuHeight = 600;
    private const int HorizontalPadding = 64;
    private const int ButtonWidth = 220;
    private const int ButtonHeight = 64;

    private readonly Action ConfirmWork;
    private readonly IReadOnlyList<JobRow> Jobs;
    private readonly string Title;
    private readonly string Subtitle;
    private readonly string WageText;
    private readonly string JobsHeading;
    private readonly string EnabledText;
    private readonly string DisabledText;
    private readonly ClickableComponent ConfirmButton;
    private readonly ClickableComponent CancelButton;

    public HiringMenu(ModConfig config, ITranslationHelper translation, Action confirmWork)
        : base(
            Game1.uiViewport.Width / 2 - MenuWidth / 2,
            Game1.uiViewport.Height / 2 - MenuHeight / 2,
            MenuWidth,
            MenuHeight,
            showUpperRightCloseButton: true)
    {
        this.ConfirmWork = confirmWork;
        this.Title = translation.Get("menu.title");
        this.Subtitle = translation.Get("menu.subtitle");
        this.WageText = translation.Get("menu.wage", new { gold = config.DailyWage });
        this.JobsHeading = translation.Get("menu.jobs");
        this.EnabledText = translation.Get("menu.enabled");
        this.DisabledText = translation.Get("menu.disabled");
        this.Jobs = new List<JobRow>
        {
            new(translation.Get("menu.job.water"), config.WaterCrops),
            new(translation.Get("menu.job.harvest"), config.HarvestCrops),
            new(translation.Get("menu.job.clear"), config.ClearDebris),
            new(translation.Get("menu.job.fertilize"), config.FertilizeEmptyDirt),
            new(translation.Get("menu.job.plant"), config.PlantSeedsFromInventory)
        };

        int buttonY = this.yPositionOnScreen + this.height - ButtonHeight - 56;
        this.ConfirmButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + HorizontalPadding, buttonY, ButtonWidth, ButtonHeight),
            translation.Get("menu.confirm"));
        this.CancelButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width - HorizontalPadding - ButtonWidth, buttonY, ButtonWidth, ButtonHeight),
            translation.Get("menu.cancel"));

        this.ConfirmButton.myID = 100;
        this.ConfirmButton.rightNeighborID = 101;
        this.CancelButton.myID = 101;
        this.CancelButton.leftNeighborID = 100;
        this.allClickableComponents = new List<ClickableComponent>
        {
            this.ConfirmButton,
            this.CancelButton,
            this.upperRightCloseButton
        };
        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.ConfirmButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            this.exitThisMenu(playSound: false);
            this.ConfirmWork();
            return;
        }

        if (this.CancelButton.containsPoint(x, y))
        {
            this.exitThisMenu();
            return;
        }

        base.receiveLeftClick(x, y, playSound);
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
        int contentY = this.yPositionOnScreen + 52;

        batch.DrawString(Game1.dialogueFont, this.Title, new Vector2(contentX, contentY), Game1.textColor);
        contentY += 72;
        batch.DrawString(Game1.smallFont, this.Subtitle, new Vector2(contentX, contentY), Game1.textColor);
        contentY += 52;
        batch.DrawString(Game1.smallFont, this.WageText, new Vector2(contentX, contentY), Game1.textColor);
        contentY += 56;
        batch.DrawString(Game1.smallFont, this.JobsHeading, new Vector2(contentX, contentY), Game1.textColor);
        contentY += 44;

        foreach (JobRow job in this.Jobs)
        {
            string state = job.Enabled ? this.EnabledText : this.DisabledText;
            Color stateColor = job.Enabled ? Color.DarkGreen : Color.DimGray;
            batch.DrawString(Game1.smallFont, job.Name, new Vector2(contentX + 24, contentY), Game1.textColor);

            Vector2 stateSize = Game1.smallFont.MeasureString(state);
            batch.DrawString(
                Game1.smallFont,
                state,
                new Vector2(this.xPositionOnScreen + this.width - HorizontalPadding - stateSize.X, contentY),
                stateColor);
            contentY += 40;
        }

        this.DrawButton(batch, this.ConfirmButton);
        this.DrawButton(batch, this.CancelButton);
        base.draw(batch);
        this.drawMouse(batch);
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
            1f,
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

    private sealed record JobRow(string Name, bool Enabled);
}
