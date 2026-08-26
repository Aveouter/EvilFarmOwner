using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class WorkContractPreviewMenu : IClickableMenu
{
    private const int MaximumWidth = 820;
    private const int MaximumHeight = 580;
    private const int ScreenMargin = 64;
    private const int HorizontalPadding = 56;
    private const int BackButtonWidth = 168;
    private const int ConfirmButtonWidth = 260;
    private const int ButtonHeight = 52;

    private readonly WorkerRosterEntry Worker;
    private readonly IReadOnlyList<WorkerRosterEntry> Workers;
    private readonly WorkContractPreview Preview;
    private readonly NamedFarmTask Task;
    private readonly ITranslationHelper Translation;
    private readonly Action ReturnToRoster;
    private readonly Func<HarvestDestinationMode, bool> ConfirmContract;
    private readonly ClickableComponent BackButton;
    private readonly ClickableComponent ConfirmButton;
    private readonly ClickableComponent? DestinationButton;
    private HarvestDestinationMode DestinationMode;

    public WorkContractPreviewMenu(
        WorkerRosterEntry worker,
        WorkContractPreview preview,
        NamedFarmTask task,
        ITranslationHelper translation,
        Action returnToRoster,
        Func<HarvestDestinationMode, bool> confirmContract,
        HarvestDestinationMode defaultDestination = HarvestDestinationMode.ClassifiedChests)
        : this(
            new[] { worker },
            preview,
            task,
            translation,
            returnToRoster,
            confirmContract,
            defaultDestination)
    {
    }

    public WorkContractPreviewMenu(
        IReadOnlyList<WorkerRosterEntry> workers,
        WorkContractPreview preview,
        NamedFarmTask task,
        ITranslationHelper translation,
        Action returnToRoster,
        Func<HarvestDestinationMode, bool> confirmContract,
        HarvestDestinationMode defaultDestination = HarvestDestinationMode.ClassifiedChests)
        : base(GetMenuX(), GetMenuY(), GetMenuWidth(), GetMenuHeight(), showUpperRightCloseButton: true)
    {
        if (workers.Count == 0)
            throw new ArgumentException("At least one worker is required.", nameof(workers));
        this.Workers = workers.ToArray();
        this.Worker = this.Workers[0];
        this.Preview = preview;
        this.Task = task;
        this.Translation = translation;
        this.ReturnToRoster = returnToRoster;
        this.ConfirmContract = confirmContract;
        this.DestinationMode = Enum.IsDefined(defaultDestination)
            ? defaultDestination
            : HarvestDestinationMode.ClassifiedChests;

        int buttonY = this.yPositionOnScreen + this.height - ButtonHeight - 28;
        this.BackButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + HorizontalPadding, buttonY, BackButtonWidth, ButtonHeight),
            translation.Get("contract.back"))
        {
            myID = 100,
            rightNeighborID = 101
        };
        this.ConfirmButton = new ClickableComponent(
            new Rectangle(this.xPositionOnScreen + this.width - HorizontalPadding - ConfirmButtonWidth, buttonY, ConfirmButtonWidth, ButtonHeight),
            this.GetConfirmText())
        {
            myID = 101,
            leftNeighborID = 100
        };

        if (task is NamedFarmTask.FarmWork or NamedFarmTask.Harvesting)
        {
            this.DestinationButton = new ClickableComponent(
                new Rectangle(this.xPositionOnScreen + HorizontalPadding, this.yPositionOnScreen + 270, this.width - HorizontalPadding * 2, 48),
                this.GetDestinationText())
            {
                myID = 102,
                downNeighborID = 101
            };
            this.ConfirmButton.upNeighborID = 102;
            this.BackButton.upNeighborID = 102;
        }

        this.allClickableComponents = new List<ClickableComponent> { this.BackButton, this.ConfirmButton };
        if (this.DestinationButton is not null)
            this.allClickableComponents.Add(this.DestinationButton);
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);
        this.snapToDefaultClickableComponent();
    }

    private int MaximumAuthorizedWage => this.Workers.Sum(worker => worker.WagePreview.MaximumAuthorizedWage);

    private bool CanAfford => Game1.player.Money >= this.MaximumAuthorizedWage;

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.DestinationButton?.containsPoint(x, y) == true)
        {
            this.DestinationMode = this.DestinationMode == HarvestDestinationMode.ClassifiedChests
                ? HarvestDestinationMode.RequesterInventory
                : HarvestDestinationMode.ClassifiedChests;
            this.DestinationButton.name = this.GetDestinationText();
            Game1.playSound("shwip");
            return;
        }

        if (this.ConfirmButton.containsPoint(x, y))
        {
            if (!this.CanAfford)
            {
                Game1.playSound("cancel");
                return;
            }

            Game1.playSound("smallSelect");
            if (this.ConfirmContract(this.DestinationMode))
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
        this.currentlySnappedComponent = this.getComponentWithID(this.CanAfford
            ? this.ConfirmButton.myID
            : this.BackButton.myID);
        this.snapCursorToCurrentSnappedComponent();
    }

    public override void draw(SpriteBatch batch)
    {
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
        int x = this.xPositionOnScreen + HorizontalPadding;
        int width = this.width - HorizontalPadding * 2;
        int y = this.yPositionOnScreen + 40;

        batch.DrawString(Game1.dialogueFont, this.Translation.Get("contract.confirm.title"), new Vector2(x, y), Game1.textColor);
        y += 64;
        this.DrawWorkerCard(batch, x, y, width);
        y += 112;
        this.DrawSummaryRow(batch, x, y, width, "contract.task", this.Translation.Get(this.GetTaskKey("contract.task")));
        y += 54;
        if (this.DestinationButton is not null)
        {
            this.DrawSelectableRow(batch, this.DestinationButton, "contract.destination.label", this.GetDestinationText());
            y += 54;
        }
        this.DrawSummaryRow(batch, x, y, width, "contract.day", this.GetDayText());

        string total = this.Translation.Get("contract.authorization.total", new { gold = this.MaximumAuthorizedWage });
        Vector2 totalSize = Game1.smallFont.MeasureString(total);
        batch.DrawString(Game1.smallFont, total, new Vector2(x + width - totalSize.X, y), new Color(35, 110, 45));

        if (!this.CanAfford)
        {
            string warning = Game1.parseText(this.Translation.Get("contract.warning.insufficient-funds", new
            {
                gold = this.MaximumAuthorizedWage
            }), Game1.smallFont, width);
            batch.DrawString(Game1.smallFont, warning, new Vector2(x, this.BackButton.bounds.Y - 40), new Color(150, 45, 40));
        }

        this.DrawButton(batch, this.BackButton, enabled: true);
        this.DrawButton(batch, this.ConfirmButton, this.CanAfford);
        base.draw(batch);
        this.drawMouse(batch);
    }

    private void DrawWorkerCard(SpriteBatch batch, int x, int y, int width)
    {
        IClickableMenu.drawTextureBox(batch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, 96, Color.White, 0.8f, false);
        int portraitSize = Math.Min(64, Math.Min(this.Worker.Portrait.Width, this.Worker.Portrait.Height));
        batch.Draw(this.Worker.Portrait, new Rectangle(x + 16, y + 16, 64, 64), new Rectangle(0, 0, portraitSize, portraitSize), Color.White);
        string workerTitle = this.Workers.Count == 1
            ? this.Worker.DisplayName
            : this.Translation.Get("contract.workers.title", new { count = this.Workers.Count });
        batch.DrawString(Game1.dialogueFont, workerTitle, new Vector2(x + 100, y + 12), Game1.textColor);
        string details = this.Workers.Count == 1
            ? this.Translation.Get("roster.worker.friendship", new { hearts = this.Preview.FriendshipHearts })
            : string.Join(" · ", this.Workers.Select(worker => worker.DisplayName));
        batch.DrawString(
            Game1.smallFont,
            Game1.parseText(details, Game1.smallFont, width - 310),
            new Vector2(x + 102, y + 58),
            Color.DimGray);
        string wage = this.Translation.Get("contract.worker.subtotal", new { gold = this.MaximumAuthorizedWage });
        Vector2 wageSize = Game1.smallFont.MeasureString(wage);
        batch.DrawString(Game1.smallFont, wage, new Vector2(x + width - wageSize.X - 20, y + 38), new Color(35, 110, 45));
    }

    private void DrawSummaryRow(SpriteBatch batch, int x, int y, int width, string labelKey, string value)
    {
        batch.DrawString(Game1.smallFont, this.Translation.Get(labelKey), new Vector2(x, y), Color.DimGray);
        string wrapped = Game1.parseText(value, Game1.smallFont, width - 190);
        batch.DrawString(Game1.smallFont, wrapped, new Vector2(x + 190, y), Game1.textColor);
    }

    private void DrawSelectableRow(SpriteBatch batch, ClickableComponent button, string labelKey, string value)
    {
        bool selected = button == this.currentlySnappedComponent || button.containsPoint(Game1.getMouseX(), Game1.getMouseY());
        if (selected)
            IClickableMenu.drawTextureBox(batch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, new Color(255, 245, 190), 0.5f, false);
        batch.DrawString(Game1.smallFont, this.Translation.Get(labelKey), new Vector2(button.bounds.X, button.bounds.Y + 8), Color.DimGray);
        Vector2 size = Game1.smallFont.MeasureString(value);
        batch.DrawString(Game1.smallFont, value, new Vector2(button.bounds.Right - size.X - 16, button.bounds.Y + 8), Game1.textColor);
    }

    private void DrawButton(SpriteBatch batch, ClickableComponent button, bool enabled)
    {
        IClickableMenu.drawTextureBox(batch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, enabled ? Color.White : Color.Gray, 0.8f, false);
        Vector2 size = Game1.smallFont.MeasureString(button.name);
        batch.DrawString(Game1.smallFont, button.name, new Vector2(button.bounds.Center.X - size.X / 2, button.bounds.Center.Y - size.Y / 2), enabled ? Game1.textColor : Color.DimGray);
    }

    private string GetDayText() => this.Preview.DayKind == ContractDayKind.RestDay
        ? this.Translation.Get("contract.day.rest")
        : this.Translation.Get("contract.day.regular");

    private string GetConfirmText() => this.Translation.Get(this.GetTaskKey(this.Preview.DayKind == ContractDayKind.RestDay
        ? "contract.confirm.rest-day"
        : "contract.confirm.regular"));

    private string GetTaskKey(string prefix)
    {
        string suffix = this.Task switch
        {
            NamedFarmTask.FarmWork => "farm-work",
            NamedFarmTask.Watering => "watering",
            NamedFarmTask.Harvesting => "harvesting",
            NamedFarmTask.StorageSorting => "storage-sorting",
            _ => throw new InvalidOperationException($"Unsupported contract task {this.Task}.")
        };
        return $"{prefix}.{suffix}";
    }

    private string GetDestinationText() => this.Translation.Get(this.DestinationMode == HarvestDestinationMode.RequesterInventory
        ? "contract.destination.requester"
        : "contract.destination.chests");

    private static int GetMenuWidth() => Math.Min(MaximumWidth, Math.Max(620, Game1.uiViewport.Width - ScreenMargin));
    private static int GetMenuHeight() => Math.Min(MaximumHeight, Math.Max(520, Game1.uiViewport.Height - ScreenMargin));
    private static int GetMenuX() => Game1.uiViewport.Width / 2 - GetMenuWidth() / 2;
    private static int GetMenuY() => Game1.uiViewport.Height / 2 - GetMenuHeight() / 2;
}
