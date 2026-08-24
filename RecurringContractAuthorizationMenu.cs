using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace EvilFarmOwner;

internal sealed class RecurringContractAuthorizationMenu : IClickableMenu
{
    private const int MenuWidth = 860;
    private const int MenuHeight = 660;
    private const int HorizontalPadding = 56;
    private const int ButtonHeight = 58;

    private readonly WorkerRosterEntry PreferredWorker;
    private readonly NamedFarmTask Task;
    private readonly IReadOnlyList<WorkerRosterEntry> AvailableWorkers;
    private readonly RecurringContractCoordinator Coordinator;
    private readonly ITranslationHelper Translation;
    private readonly Action ReturnToTaskSelection;
    private readonly Action Saved;
    private readonly IReadOnlyList<string> SubstituteNames;
    private readonly int FixedRegularCap;
    private readonly int PoolRegularCap;
    private readonly int FixedRestCap;
    private readonly int PoolRestCap;
    private readonly ClickableComponent FixedButton;
    private readonly ClickableComponent SubstitutesButton;
    private readonly ClickableComponent RestDayButton;
    private readonly ClickableComponent BackButton;
    private bool AllowRestDays;

    public RecurringContractAuthorizationMenu(
        WorkerRosterEntry preferredWorker,
        NamedFarmTask task,
        IReadOnlyList<WorkerRosterEntry> availableWorkers,
        RecurringContractCoordinator coordinator,
        ITranslationHelper translation,
        Action returnToTaskSelection,
        Action saved)
        : base(
            Game1.uiViewport.Width / 2 - Math.Min(MenuWidth, Game1.uiViewport.Width - 64) / 2,
            Game1.uiViewport.Height / 2 - Math.Min(MenuHeight, Game1.uiViewport.Height - 64) / 2,
            Math.Min(MenuWidth, Game1.uiViewport.Width - 64),
            Math.Min(MenuHeight, Game1.uiViewport.Height - 64),
            showUpperRightCloseButton: true)
    {
        this.PreferredWorker = preferredWorker;
        this.Task = task;
        this.AvailableWorkers = availableWorkers;
        this.Coordinator = coordinator;
        this.Translation = translation;
        this.ReturnToTaskSelection = returnToTaskSelection;
        this.Saved = saved;
        this.SubstituteNames = this.AvailableWorkers
            .Select(worker => worker.InternalName)
            .Where(name => !string.Equals(name, this.PreferredWorker.InternalName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        this.FixedRegularCap = this.GetCap(new[] { this.PreferredWorker.InternalName }, restDay: false);
        this.PoolRegularCap = this.GetCap(
            new[] { this.PreferredWorker.InternalName }.Concat(this.SubstituteNames),
            restDay: false);
        this.FixedRestCap = this.GetCap(new[] { this.PreferredWorker.InternalName }, restDay: true);
        this.PoolRestCap = this.GetCap(
            new[] { this.PreferredWorker.InternalName }.Concat(this.SubstituteNames),
            restDay: true);

        int contentX = this.xPositionOnScreen + HorizontalPadding;
        int buttonWidth = this.width - HorizontalPadding * 2;
        int backY = this.yPositionOnScreen + this.height - 66;
        int restDayY = backY - 60;
        int substitutesY = restDayY - ButtonHeight - 12;
        int fixedY = substitutesY - ButtonHeight - 12;
        this.FixedButton = new ClickableComponent(
            new Rectangle(contentX, fixedY, buttonWidth, ButtonHeight), "")
        {
            myID = 100,
            downNeighborID = 101
        };
        this.SubstitutesButton = new ClickableComponent(
            new Rectangle(contentX, substitutesY, buttonWidth, ButtonHeight), "")
        {
            myID = 101,
            upNeighborID = 100,
            downNeighborID = 102
        };
        this.RestDayButton = new ClickableComponent(
            new Rectangle(contentX, restDayY, buttonWidth, 48), "")
        {
            myID = 102,
            upNeighborID = 101,
            downNeighborID = 103
        };
        this.BackButton = new ClickableComponent(
            new Rectangle(contentX, backY, 180, 46),
            this.Translation.Get("contract.back"))
        {
            myID = 103,
            upNeighborID = 102
        };
        this.RefreshLabels();
        this.allClickableComponents = new List<ClickableComponent>
        {
            this.FixedButton,
            this.SubstitutesButton,
            this.RestDayButton,
            this.BackButton
        };
        if (this.upperRightCloseButton is not null)
            this.allClickableComponents.Add(this.upperRightCloseButton);
        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.FixedButton.containsPoint(x, y))
        {
            this.Save(allowSubstitutes: false);
            return;
        }

        if (this.SubstitutesButton.containsPoint(x, y) && this.SubstituteNames.Count > 0)
        {
            this.Save(allowSubstitutes: true);
            return;
        }

        if (this.RestDayButton.containsPoint(x, y))
        {
            this.AllowRestDays = !this.AllowRestDays;
            Game1.playSound("drumkit6");
            this.RefreshLabels();
            return;
        }

        if (this.BackButton.containsPoint(x, y))
        {
            Game1.playSound("bigDeSelect");
            this.ReturnToTaskSelection();
            return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Back)
        {
            Game1.playSound("bigDeSelect");
            this.ReturnToTaskSelection();
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void snapToDefaultClickableComponent()
    {
        this.currentlySnappedComponent = this.getComponentWithID(this.FixedButton.myID);
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

        int x = this.xPositionOnScreen + HorizontalPadding;
        int width = this.width - HorizontalPadding * 2;
        int y = this.yPositionOnScreen + 40;
        batch.DrawString(Game1.dialogueFont, this.Translation.Get("recurring.authorize.title"), new Vector2(x, y), Game1.textColor);
        y += 62;
        string subtitle = Game1.parseText(
            this.Translation.Get("recurring.authorize.subtitle"),
            Game1.smallFont,
            width);
        batch.DrawString(Game1.smallFont, subtitle, new Vector2(x, y), Color.DimGray);
        y += 64;

        string task = this.Translation.Get(this.Task == NamedFarmTask.Watering
            ? "contract.task.watering"
            : "contract.task.harvesting");
        string substituteList = this.SubstituteNames.Count == 0
            ? this.Translation.Get("recurring.value.none")
            : string.Join(", ", this.SubstituteNames.Select(this.GetDisplayName));
        this.DrawLine(batch, "recurring.manage.task", task, x, ref y);
        this.DrawLine(batch, "recurring.manage.preferred", this.PreferredWorker.DisplayName, x, ref y);
        this.DrawLine(batch, "recurring.authorize.fixed-cap", this.Translation.Get("contract.gold", new { gold = this.FixedRegularCap }), x, ref y);
        this.DrawLine(batch, "recurring.authorize.pool-cap", this.Translation.Get("contract.gold", new { gold = this.PoolRegularCap }), x, ref y);
        string wrappedSubstitutes = Game1.parseText(
            this.Translation.Get("recurring.authorize.substitute-list", new { workers = substituteList }),
            Game1.smallFont,
            width);
        batch.DrawString(Game1.smallFont, wrappedSubstitutes, new Vector2(x, y), Color.DimGray);

        this.DrawButton(batch, this.FixedButton, enabled: true);
        this.DrawButton(batch, this.SubstitutesButton, this.SubstituteNames.Count > 0);
        this.DrawButton(batch, this.RestDayButton, enabled: true);
        this.DrawButton(batch, this.BackButton, enabled: true);
        base.draw(batch);
        this.drawMouse(batch);
    }

    private void Save(bool allowSubstitutes)
    {
        bool saved = this.Coordinator.CreateTemplate(
            this.PreferredWorker.InternalName,
            this.Task,
            this.SubstituteNames,
            allowSubstitutes,
            this.AllowRestDays);
        if (!saved)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("recurring.hud.create-failed"),
                HUDMessage.error_type));
            return;
        }

        Game1.playSound("newArtifact");
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("recurring.hud.created"),
            HUDMessage.newQuest_type));
        this.Saved();
    }

    private void RefreshLabels()
    {
        this.FixedButton.name = this.Translation.Get("recurring.authorize.fixed");
        this.SubstitutesButton.name = this.Translation.Get("recurring.authorize.substitutes", new
        {
            count = this.SubstituteNames.Count
        });
        this.RestDayButton.name = this.Translation.Get(this.AllowRestDays
            ? "recurring.authorize.rest-on"
            : "recurring.authorize.rest-off");
        if (this.AllowRestDays)
        {
            this.RestDayButton.name = this.Translation.Get("recurring.authorize.rest-on", new
            {
                fixedGold = this.FixedRestCap,
                poolGold = this.PoolRestCap
            });
        }
    }

    private int GetCap(IEnumerable<string> names, bool restDay)
    {
        int dayOfMonth = restDay ? 6 : 1;
        return names.Max(name =>
        {
            int hearts = Game1.player.getFriendshipHeartLevelForNPC(name);
            return ContractPreviewService.Create(hearts, dayOfMonth, name, this.Task).MaximumAuthorizedWage;
        });
    }

    private string GetDisplayName(string internalName)
    {
        return this.AvailableWorkers
            .FirstOrDefault(worker => string.Equals(
                worker.InternalName,
                internalName,
                StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? internalName;
    }

    private void DrawLine(SpriteBatch batch, string labelKey, string value, int x, ref int y)
    {
        batch.DrawString(Game1.smallFont, this.Translation.Get(labelKey), new Vector2(x, y), Color.DimGray);
        batch.DrawString(Game1.smallFont, value, new Vector2(x + 230, y), Game1.textColor);
        y += 34;
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
}
