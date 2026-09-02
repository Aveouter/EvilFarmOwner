using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class LostAndFoundMenu : IClickableMenu
{
    private const int MenuWidth = 620;
    private const int MenuHeight = 340;
    private const int HorizontalPadding = 56;
    private const int RowHeight = 64;

    private readonly ITranslationHelper Translation;
    private readonly int OverflowCount;
    private readonly int QuarantineCount;
    private readonly Action? OpenOverflow;
    private readonly Action? OpenQuarantine;
    private readonly ClickableComponent OverflowRow;
    private readonly ClickableComponent QuarantineRow;
    private readonly ClickableComponent CloseButton;

    public LostAndFoundMenu(
        ITranslationHelper translation,
        int overflowCount,
        int quarantineCount,
        Action? openOverflow,
        Action? openQuarantine)
        : base(
            Game1.uiViewport.Width / 2 - MenuWidth / 2,
            Game1.uiViewport.Height / 2 - MenuHeight / 2,
            MenuWidth,
            MenuHeight,
            showUpperRightCloseButton: true)
    {
        this.Translation = translation;
        this.OverflowCount = overflowCount;
        this.QuarantineCount = quarantineCount;
        this.OpenOverflow = openOverflow;
        this.OpenQuarantine = openQuarantine;

        int contentX = this.xPositionOnScreen + HorizontalPadding;
        int contentWidth = this.width - HorizontalPadding * 2;
        int rowY = this.yPositionOnScreen + 100;
        this.OverflowRow = new ClickableComponent(
            new Rectangle(contentX, rowY, contentWidth, RowHeight),
            "overflow")
        {
            myID = 100,
            upNeighborID = -1,
            downNeighborID = 101
        };
        this.QuarantineRow = new ClickableComponent(
            new Rectangle(contentX, rowY + RowHeight + 12, contentWidth, RowHeight),
            "quarantine")
        {
            myID = 101,
            upNeighborID = 100,
            downNeighborID = 102
        };
        this.CloseButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width / 2 - 110, this.yPositionOnScreen + this.height - 76, 220, 44),
            translation.Get("lost-found.close"))
        {
            myID = 102,
            upNeighborID = 101
        };

        this.allClickableComponents = new List<ClickableComponent>
        {
            this.OverflowRow,
            this.QuarantineRow,
            this.CloseButton
        };
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);
        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.OverflowRow.containsPoint(x, y) && this.OverflowCount > 0)
        {
            Game1.playSound("smallSelect");
            Game1.activeClickableMenu = null;
            this.OpenOverflow?.Invoke();
            return;
        }

        if (this.QuarantineRow.containsPoint(x, y)
            && this.QuarantineCount > 0
            && this.OpenQuarantine is not null)
        {
            Game1.playSound("smallSelect");
            Game1.activeClickableMenu = null;
            this.OpenQuarantine.Invoke();
            return;
        }

        if (this.CloseButton.containsPoint(x, y))
        {
            Game1.playSound("bigDeSelect");
            Game1.activeClickableMenu = null;
            return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Back)
        {
            Game1.playSound("bigDeSelect");
            Game1.activeClickableMenu = null;
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void snapToDefaultClickableComponent()
    {
        this.currentlySnappedComponent = this.getComponentWithID(this.OverflowRow.myID);
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
        batch.DrawString(
            Game1.dialogueFont,
            this.Translation.Get("lost-found.title"),
            new Vector2(contentX, this.yPositionOnScreen + 40),
            Game1.textColor);

        if (this.OverflowCount == 0 && this.QuarantineCount == 0)
        {
            batch.DrawString(
                Game1.smallFont,
                this.Translation.Get("lost-found.empty"),
                new Vector2(contentX + 8, this.yPositionOnScreen + 140),
                Color.DimGray);
        }
        else
        {
            this.DrawRow(
                batch,
                this.OverflowRow,
                this.OverflowCount == 0
                    ? this.Translation.Get("lost-found.overflow.empty")
                    : this.Translation.Get("lost-found.overflow", new { count = this.OverflowCount }));
            this.DrawRow(
                batch,
                this.QuarantineRow,
                this.QuarantineCount == 0
                    ? this.Translation.Get("lost-found.quarantine.empty")
                    : this.Translation.Get("lost-found.quarantine", new { count = this.QuarantineCount }));
            if (this.OpenQuarantine is null && this.QuarantineCount > 0)
            {
                string hostOnly = this.Translation.Get("lost-found.host-only");
                Vector2 hostOnlySize = Game1.smallFont.MeasureString(hostOnly);
                batch.DrawString(
                    Game1.smallFont,
                    hostOnly,
                    new Vector2(
                        this.QuarantineRow.bounds.Right - hostOnlySize.X - 16,
                        this.QuarantineRow.bounds.Center.Y - hostOnlySize.Y / 2),
                    Color.DimGray);
            }
        }

        this.DrawButton(batch, this.CloseButton);
        base.draw(batch);
        this.drawMouse(batch);
    }

    private void DrawRow(SpriteBatch batch, ClickableComponent row, string label)
    {
        bool hovered = row.containsPoint(Game1.getMouseX(), Game1.getMouseY());
        IClickableMenu.drawTextureBox(
            batch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            row.bounds.X,
            row.bounds.Y,
            row.bounds.Width,
            row.bounds.Height,
            hovered ? new Color(255, 245, 190) : Color.White,
            0.8f,
            drawShadow: false);

        batch.DrawString(
            Game1.smallFont,
            label,
            new Vector2(row.bounds.X + 16, row.bounds.Center.Y - Game1.smallFont.MeasureString(label).Y / 2),
            Game1.textColor);
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
}
