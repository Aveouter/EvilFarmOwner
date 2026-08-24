using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class RecurringContractMenu : IClickableMenu
{
    private const int MenuWidth = 820;
    private const int MenuHeight = 650;
    private const int HorizontalPadding = 56;
    private const int ButtonHeight = 52;

    private readonly RecurringContractCoordinator Coordinator;
    private readonly ITranslationHelper Translation;
    private readonly Action CreateTemplate;
    private readonly IReadOnlyDictionary<string, string> DisplayNames;
    private readonly ClickableComponent CreateButton;
    private readonly ClickableComponent ToggleButton;
    private readonly ClickableComponent DeleteButton;
    private readonly ClickableComponent CloseButton;

    public RecurringContractMenu(
        RecurringContractCoordinator coordinator,
        ITranslationHelper translation,
        Action createTemplate)
        : base(
            Game1.uiViewport.Width / 2 - Math.Min(MenuWidth, Game1.uiViewport.Width - 64) / 2,
            Game1.uiViewport.Height / 2 - Math.Min(MenuHeight, Game1.uiViewport.Height - 64) / 2,
            Math.Min(MenuWidth, Game1.uiViewport.Width - 64),
            Math.Min(MenuHeight, Game1.uiViewport.Height - 64),
            showUpperRightCloseButton: true)
    {
        this.Coordinator = coordinator;
        this.Translation = translation;
        this.CreateTemplate = createTemplate;
        this.DisplayNames = Utility.getAllCharacters()
            .Where(npc => !string.IsNullOrWhiteSpace(npc.Name))
            .GroupBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().displayName)
                    ? group.Key
                    : group.First().displayName,
                StringComparer.OrdinalIgnoreCase);

        int buttonY = this.yPositionOnScreen + this.height - ButtonHeight - 32;
        int availableWidth = this.width - HorizontalPadding * 2;
        int gap = 12;
        int buttonWidth = (availableWidth - gap * 3) / 4;
        this.CreateButton = this.CreateButtonAt(0, buttonY, buttonWidth, gap, "recurring.manage.create", 100);
        this.ToggleButton = this.CreateButtonAt(1, buttonY, buttonWidth, gap, "recurring.manage.pause", 101);
        this.DeleteButton = this.CreateButtonAt(2, buttonY, buttonWidth, gap, "recurring.manage.delete", 102);
        this.CloseButton = this.CreateButtonAt(3, buttonY, buttonWidth, gap, "recurring.manage.close", 103);
        this.RefreshButtonLabels();

        this.allClickableComponents = new List<ClickableComponent>
        {
            this.CreateButton,
            this.ToggleButton,
            this.DeleteButton,
            this.CloseButton
        };
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);
        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.CreateButton.containsPoint(x, y) && this.Coordinator.IsPersistenceHealthy)
        {
            Game1.playSound("smallSelect");
            this.CreateTemplate();
            return;
        }

        if (this.ToggleButton.containsPoint(x, y) && this.Coordinator.Template is { } template)
        {
            bool changed = template.Enabled ? this.Coordinator.Pause() : this.Coordinator.Resume();
            if (changed)
            {
                Game1.playSound("smallSelect");
                this.RefreshButtonLabels();
            }
            return;
        }

        if (this.DeleteButton.containsPoint(x, y) && this.Coordinator.Delete())
        {
            Game1.playSound("trashcan");
            this.RefreshButtonLabels();
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
        this.currentlySnappedComponent = this.getComponentWithID(this.CreateButton.myID);
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
        int y = this.yPositionOnScreen + 40;
        batch.DrawString(Game1.dialogueFont, this.Translation.Get("recurring.manage.title"), new Vector2(contentX, y), Game1.textColor);
        y += 62;

        if (!this.Coordinator.IsPersistenceHealthy)
        {
            this.DrawWrapped(batch, "recurring.manage.invalid", contentX, y, contentWidth, new Color(150, 45, 40));
        }
        else if (this.Coordinator.Template is not { } template)
        {
            this.DrawWrapped(batch, "recurring.manage.empty", contentX, y, contentWidth, Color.DimGray);
        }
        else
        {
            string task = this.Translation.Get(template.Task == NamedFarmTask.Watering
                ? "contract.task.watering"
                : "contract.task.harvesting");
            string mode = this.Translation.Get(template.WorkerMode == RecurringWorkerMode.FixedWorkerOnly
                ? "recurring.mode.fixed"
                : "recurring.mode.substitutes");
            string substitutes = template.ApprovedSubstituteNames.Length == 0
                ? this.Translation.Get("recurring.value.none")
                : string.Join(", ", template.ApprovedSubstituteNames.Select(this.GetDisplayName));
            string restDays = template.AllowRestDays
                ? this.Translation.Get("recurring.value.rest-enabled", new { gold = template.MaximumRestDayGold })
                : this.Translation.Get("recurring.value.rest-disabled");

            this.DrawLine(batch, "recurring.manage.state", template.Enabled
                ? this.Translation.Get("recurring.value.enabled")
                : this.Translation.Get("recurring.value.paused"), contentX, ref y);
            this.DrawLine(batch, "recurring.manage.task", task, contentX, ref y);
            this.DrawLine(batch, "recurring.manage.preferred", this.GetDisplayName(template.PreferredWorkerName), contentX, ref y);
            this.DrawLine(batch, "recurring.manage.mode", mode, contentX, ref y);
            this.DrawLine(batch, "recurring.manage.regular-cap", this.Translation.Get("contract.gold", new { gold = template.MaximumRegularDayGold }), contentX, ref y);
            this.DrawLine(batch, "recurring.manage.rest", restDays, contentX, ref y);
            this.DrawWrappedValue(batch, "recurring.manage.substitutes", substitutes, contentX, ref y, contentWidth);

            y += 10;
            this.DrawWrappedValue(
                batch,
                "recurring.manage.latest",
                this.GetLatestText(template.LastEvaluation),
                contentX,
                ref y,
                contentWidth);
            if (template.LastEvaluation.Rejections.Length > 0)
            {
                string rejectionText = string.Join("; ", template.LastEvaluation.Rejections
                    .Take(3)
                    .Select(rejection => this.Translation.Get("recurring.rejection", new
                    {
                        worker = this.GetDisplayName(rejection.WorkerName),
                        reason = this.Translation.Get(rejection.ReasonKey)
                    })));
                this.DrawWrappedValue(batch, "recurring.manage.rejections", rejectionText, contentX, ref y, contentWidth);
            }
        }

        this.DrawButton(batch, this.CreateButton, this.Coordinator.IsPersistenceHealthy);
        this.DrawButton(batch, this.ToggleButton, this.Coordinator.Template is not null);
        this.DrawButton(
            batch,
            this.DeleteButton,
            this.Coordinator.Template is not null || !this.Coordinator.IsPersistenceHealthy);
        this.DrawButton(batch, this.CloseButton, enabled: true);
        base.draw(batch);
        this.drawMouse(batch);
    }

    private ClickableComponent CreateButtonAt(
        int index,
        int y,
        int width,
        int gap,
        string translationKey,
        int id)
    {
        ClickableComponent button = new(
            new Rectangle(
                this.xPositionOnScreen + HorizontalPadding + index * (width + gap),
                y,
                width,
                ButtonHeight),
            this.Translation.Get(translationKey))
        {
            myID = id,
            leftNeighborID = index == 0 ? -1 : id - 1,
            rightNeighborID = index == 3 ? -1 : id + 1
        };
        return button;
    }

    private void RefreshButtonLabels()
    {
        this.CreateButton.name = this.Translation.Get(this.Coordinator.Template is null
            ? "recurring.manage.create"
            : "recurring.manage.replace");
        this.ToggleButton.name = this.Translation.Get(this.Coordinator.Template?.Enabled == true
            ? "recurring.manage.pause"
            : "recurring.manage.resume");
        this.DeleteButton.name = this.Translation.Get(this.Coordinator.IsPersistenceHealthy
            ? "recurring.manage.delete"
            : "recurring.manage.reset-invalid");
    }

    private void DrawLine(SpriteBatch batch, string labelKey, string value, int x, ref int y)
    {
        batch.DrawString(Game1.smallFont, this.Translation.Get(labelKey), new Vector2(x, y), Color.DimGray);
        batch.DrawString(Game1.smallFont, value, new Vector2(x + 230, y), Game1.textColor);
        y += 38;
    }

    private void DrawWrappedValue(SpriteBatch batch, string labelKey, string value, int x, ref int y, int width)
    {
        batch.DrawString(Game1.smallFont, this.Translation.Get(labelKey), new Vector2(x, y), Color.DimGray);
        string wrapped = Game1.parseText(value, Game1.smallFont, width - 230);
        batch.DrawString(Game1.smallFont, wrapped, new Vector2(x + 230, y), Game1.textColor);
        y += Math.Max(38, (int)Game1.smallFont.MeasureString(wrapped).Y + 8);
    }

    private void DrawWrapped(SpriteBatch batch, string key, int x, int y, int width, Color color)
    {
        string text = Game1.parseText(this.Translation.Get(key), Game1.smallFont, width);
        batch.DrawString(Game1.smallFont, text, new Vector2(x, y), color);
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
        Vector2 size = Game1.smallFont.MeasureString(button.name);
        batch.DrawString(
            Game1.smallFont,
            button.name,
            new Vector2(button.bounds.Center.X - size.X / 2, button.bounds.Center.Y - size.Y / 2),
            enabled ? Game1.textColor : Color.DimGray);
    }

    private string GetLatestText(RecurringEvaluationData evaluation)
    {
        return evaluation.Status switch
        {
            RecurringEvaluationStatus.None => this.Translation.Get("recurring.latest.none"),
            RecurringEvaluationStatus.Started => this.Translation.Get("recurring.latest.started", new
            {
                worker = this.GetDisplayName(evaluation.SelectedWorkerName),
                gold = evaluation.AuthorizedGold
            }),
            RecurringEvaluationStatus.Completed => this.Translation.Get("recurring.latest.completed", new
            {
                worker = this.GetDisplayName(evaluation.SelectedWorkerName),
                completed = evaluation.CompletedWork,
                paid = evaluation.ChargedGold,
                refunded = evaluation.RefundedGold
            }),
            RecurringEvaluationStatus.Stopped => this.Translation.Get("recurring.latest.stopped", new
            {
                worker = this.GetDisplayName(evaluation.SelectedWorkerName),
                reason = this.Translation.Get(evaluation.ReasonKey),
                completed = evaluation.CompletedWork,
                paid = evaluation.ChargedGold,
                refunded = evaluation.RefundedGold
            }),
            _ => this.Translation.Get("recurring.latest.skipped", new
            {
                reason = this.Translation.Get(evaluation.ReasonKey)
            })
        };
    }

    private string GetDisplayName(string internalName)
    {
        return this.DisplayNames.GetValueOrDefault(internalName, internalName);
    }
}
