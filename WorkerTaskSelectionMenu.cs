using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class WorkerTaskSelectionMenu : IClickableMenu
{
    private const int MenuWidth = 760;
    private const int MenuHeight = 520;
    private const int HorizontalPadding = 56;
    private const int ButtonHeight = 96;

    private readonly WorkerRosterEntry Worker;
    private readonly ITranslationHelper Translation;
    private readonly Action ReturnToRoster;
    private readonly Action<NamedFarmTask> SelectTask;
    private readonly ClickableComponent WateringButton;
    private readonly ClickableComponent HarvestingButton;
    private readonly ClickableComponent BackButton;

    public WorkerTaskSelectionMenu(
        WorkerRosterEntry worker,
        ITranslationHelper translation,
        Action returnToRoster,
        Action<NamedFarmTask> selectTask)
        : base(
            Game1.uiViewport.Width / 2 - Math.Min(MenuWidth, Game1.uiViewport.Width - 64) / 2,
            Game1.uiViewport.Height / 2 - Math.Min(MenuHeight, Game1.uiViewport.Height - 64) / 2,
            Math.Min(MenuWidth, Game1.uiViewport.Width - 64),
            Math.Min(MenuHeight, Game1.uiViewport.Height - 64),
            showUpperRightCloseButton: true)
    {
        this.Worker = worker;
        this.Translation = translation;
        this.ReturnToRoster = returnToRoster;
        this.SelectTask = selectTask;

        int buttonWidth = this.width - HorizontalPadding * 2;
        int buttonX = this.xPositionOnScreen + HorizontalPadding;
        int firstButtonY = this.yPositionOnScreen + 214;
        this.WateringButton = new ClickableComponent(
            new Rectangle(buttonX, firstButtonY, buttonWidth, ButtonHeight),
            translation.Get("task-selection.watering"))
        {
            myID = 100,
            downNeighborID = 101
        };
        this.HarvestingButton = new ClickableComponent(
            new Rectangle(buttonX, firstButtonY + ButtonHeight + 16, buttonWidth, ButtonHeight),
            translation.Get("task-selection.harvesting"))
        {
            myID = 101,
            upNeighborID = 100,
            downNeighborID = 102
        };
        this.BackButton = new ClickableComponent(
            new Rectangle(buttonX, this.yPositionOnScreen + this.height - 66, 180, 48),
            translation.Get("contract.back"))
        {
            myID = 102,
            upNeighborID = 101
        };

        this.allClickableComponents = new List<ClickableComponent>
        {
            this.WateringButton,
            this.HarvestingButton,
            this.BackButton
        };
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);

        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.WateringButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            this.SelectTask(NamedFarmTask.Watering);
            return;
        }

        if (this.HarvestingButton.containsPoint(x, y))
        {
            Game1.playSound("smallSelect");
            this.SelectTask(NamedFarmTask.Harvesting);
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
        this.currentlySnappedComponent = this.getComponentWithID(this.WateringButton.myID);
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
            this.Translation.Get("task-selection.title", new { worker = this.Worker.DisplayName }),
            new Vector2(contentX, contentY),
            Game1.textColor);

        contentY += 58;
        string subtitle = Game1.parseText(
            this.Translation.Get("task-selection.subtitle"),
            Game1.smallFont,
            contentWidth);
        batch.DrawString(Game1.smallFont, subtitle, new Vector2(contentX, contentY), Color.DimGray);

        this.DrawTaskButton(
            batch,
            this.WateringButton,
            this.Translation.Get("task-selection.watering.description"));
        this.DrawTaskButton(
            batch,
            this.HarvestingButton,
            this.Translation.Get("task-selection.harvesting.description"));
        this.DrawBackButton(batch);

        base.draw(batch);
        this.drawMouse(batch);
    }

    private void DrawTaskButton(SpriteBatch batch, ClickableComponent button, string description)
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

        batch.DrawString(
            Game1.smallFont,
            button.name,
            new Vector2(button.bounds.X + 20, button.bounds.Y + 15),
            Game1.textColor);
        batch.DrawString(
            Game1.smallFont,
            Game1.parseText(description, Game1.smallFont, button.bounds.Width - 40),
            new Vector2(button.bounds.X + 20, button.bounds.Y + 50),
            Color.DimGray);
    }

    private void DrawBackButton(SpriteBatch batch)
    {
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            this.BackButton.bounds.X,
            this.BackButton.bounds.Y,
            this.BackButton.bounds.Width,
            this.BackButton.bounds.Height,
            Color.White,
            0.8f,
            drawShadow: false);
        Vector2 size = Game1.smallFont.MeasureString(this.BackButton.name);
        batch.DrawString(
            Game1.smallFont,
            this.BackButton.name,
            new Vector2(
                this.BackButton.bounds.Center.X - size.X / 2,
                this.BackButton.bounds.Center.Y - size.Y / 2),
            Game1.textColor);
    }
}
