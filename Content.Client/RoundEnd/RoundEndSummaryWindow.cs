using System;
using System.Linq;
using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Client.Message;
using Content.Shared.Eclipse.Progression;
using Content.Shared.GameTicking;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.RoundEnd
{
    public sealed class RoundEndSummaryWindow : DefaultWindow
    {
        private readonly IEntityManager _entityManager;
        public int RoundId;

        public RoundEndSummaryWindow(string gm, string roundEnd, TimeSpan roundTimeSpan, int roundId,
            RoundEndMessageEvent.RoundEndPlayerInfo[] info, IEntityManager entityManager,
            EclipseRoundEndStatsEvent? shiftStats = null)
        {
            _entityManager = entityManager;

            MinSize = SetSize = new Vector2(560, 620);

            Title = Loc.GetString("round-end-summary-window-title");

            // The round end window is split into tabs: the player's personal Eclipse shift results,
            // the round stats, and a list of RoundEndPlayerInfo for each player.
            // The round stats tab is a good place for things like: "x many people died.",
            // "clown slipped the crew x times.", "x shots were fired this round.", etc.

            RoundId = roundId;
            var roundEndTabs = new TabContainer();

            // Personal Eclipse shift summary comes first when the server sent it.
            if (shiftStats != null)
                roundEndTabs.AddChild(MakeShiftStatsTab(shiftStats));

            roundEndTabs.AddChild(MakeRoundEndSummaryTab(gm, roundEnd, roundTimeSpan, roundId));
            roundEndTabs.AddChild(MakePlayerManifestTab(info));

            ContentsContainer.AddChild(roundEndTabs);

            OpenCenteredRight();
            MoveToFront();
        }

        #region Eclipse shift summary

        private static readonly Color EclipsePanelBg = Color.FromHex("#070300F2");
        private static readonly Color EclipseBorder = Color.FromHex("#A85E1268");

        private Control MakeShiftStatsTab(EclipseRoundEndStatsEvent stats)
        {
            var tab = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Name = "Смена",
            };

            var scroll = new ScrollContainer
            {
                VerticalExpand = true,
                HorizontalExpand = true,
                Margin = new Thickness(16),
            };

            var root = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 6,
            };

            root.AddChild(Gold("ИТОГИ СМЕНЫ", true));
            root.AddChild(Subtle($"Раунд #{stats.RoundId}"));
            root.AddChild(Spacer(8));

            // Summary cards.
            var cards = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 10,
            };
            cards.AddChild(MakeStatCard("Опыт за смену", $"+{stats.ExperienceEarned}", null));
            cards.AddChild(MakeStatCard("Мериты", $"+{stats.MeritsEarned}", "/Textures/Eclipse/MainMenu/merit.png"));
            cards.AddChild(MakeStatCard("Осколки", $"+{stats.ShardsEarned}", "/Textures/Eclipse/MainMenu/shard.png"));
            root.AddChild(cards);
            root.AddChild(Spacer(12));

            // Level progress.
            root.AddChild(MakeLevelPanel(stats));
            root.AddChild(Spacer(12));

            // Experience breakdown.
            root.AddChild(Gold("ОТКУДА ОПЫТ", true));
            root.AddChild(Spacer(4));
            root.AddChild(BreakdownRow("За выполненные задания", stats.TaskExperience));
            root.AddChild(BreakdownRow("За участие в раунде", stats.ParticipationExperience));
            root.AddChild(Spacer(12));

            // Completed tasks.
            root.AddChild(Gold("ВЫПОЛНЕННЫЕ ЗАДАНИЯ", true));
            root.AddChild(Spacer(4));
            if (stats.CompletedTasks.Count == 0)
            {
                root.AddChild(Subtle("За эту смену заданий не выполнено."));
            }
            else
            {
                foreach (var task in stats.CompletedTasks)
                    root.AddChild(TaskRow(task));
            }

            scroll.AddChild(root);
            tab.AddChild(scroll);
            return tab;
        }

        private Control MakeStatCard(string caption, string value, string? iconPath)
        {
            var panel = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = NewCardStyle(),
            };

            var box = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };
            box.AddChild(Subtle(caption));

            var valueRow = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                SeparationOverride = 6,
            };
            if (iconPath != null)
            {
                valueRow.AddChild(new TextureRect
                {
                    TexturePath = iconPath,
                    SetSize = new Vector2(22, 22),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    VerticalAlignment = VAlignment.Center,
                });
            }
            valueRow.AddChild(Gold(value, true));
            box.AddChild(valueRow);

            panel.AddChild(box);
            return panel;
        }

        private Control MakeLevelPanel(EclipseRoundEndStatsEvent stats)
        {
            var panel = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = NewCardStyle(),
            };

            var box = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 6,
            };
            box.AddChild(Gold($"Уровень {stats.Level} — {EclipseProgression.GetRankName(stats.Level)}", true));

            var progress = new ProgressBar
            {
                MinValue = 0f,
                MaxValue = 1f,
                Value = stats.NextLevelExperience <= 0
                    ? 1f
                    : Math.Clamp((float) stats.CurrentLevelExperience / stats.NextLevelExperience, 0f, 1f),
                MinHeight = 8,
                HorizontalExpand = true,
            };
            box.AddChild(progress);

            var remaining = stats.NextLevelExperience <= 0
                ? "Максимальный уровень достигнут."
                : $"{stats.CurrentLevelExperience} / {stats.NextLevelExperience} XP · до уровня осталось {Math.Max(0, stats.NextLevelExperience - stats.CurrentLevelExperience)} XP";
            box.AddChild(Subtle(remaining));

            panel.AddChild(box);
            return panel;
        }

        private Control BreakdownRow(string caption, int experience)
        {
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
            };
            row.AddChild(Subtle(caption));
            row.AddChild(new Control { HorizontalExpand = true });
            row.AddChild(Gold($"+{experience} XP", false));
            return row;
        }

        private Control TaskRow(string title)
        {
            var label = new RichTextLabel { HorizontalExpand = true };
            label.SetMarkup($"[color=#E6A11A]•[/color] {FormattedMessage.EscapeText(title)}");
            return label;
        }

        private static EclipseStyleBoxRounded NewCardStyle()
        {
            return new EclipseStyleBoxRounded
            {
                BackgroundColor = EclipsePanelBg,
                BorderColor = EclipseBorder,
                BorderThickness = new Thickness(1),
                Radius = 8,
                ContentMarginLeftOverride = 12,
                ContentMarginRightOverride = 12,
                ContentMarginTopOverride = 10,
                ContentMarginBottomOverride = 10,
            };
        }

        private static RichTextLabel Gold(string text, bool bold)
        {
            var label = new RichTextLabel { HorizontalExpand = true };
            var inner = FormattedMessage.EscapeText(text);
            label.SetMarkup(bold ? $"[color=#E6A11A][bold]{inner}[/bold][/color]" : $"[color=#E6A11A]{inner}[/color]");
            return label;
        }

        private static RichTextLabel Subtle(string text)
        {
            var label = new RichTextLabel { HorizontalExpand = true };
            label.SetMarkup($"[color=#A6A6A6]{FormattedMessage.EscapeText(text)}[/color]");
            return label;
        }

        private static Control Spacer(float height)
        {
            return new Control { MinSize = new Vector2(0, height) };
        }

        #endregion

        private BoxContainer MakeRoundEndSummaryTab(string gamemode, string roundEnd, TimeSpan roundDuration, int roundId)
        {
            var roundEndSummaryTab = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Name = Loc.GetString("round-end-summary-window-round-end-summary-tab-title")
            };

            var roundEndSummaryContainerScrollbox = new ScrollContainer
            {
                VerticalExpand = true,
                Margin = new Thickness(10)
            };
            var roundEndSummaryContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical
            };

            //Gamemode Name
            var gamemodeLabel = new RichTextLabel();
            var gamemodeMessage = new FormattedMessage();
            gamemodeMessage.AddMarkupOrThrow(Loc.GetString("round-end-summary-window-round-id-label", ("roundId", roundId)));
            gamemodeMessage.AddText(" ");
            gamemodeMessage.AddMarkupOrThrow(Loc.GetString("round-end-summary-window-gamemode-name-label", ("gamemode", gamemode)));
            gamemodeLabel.SetMessage(gamemodeMessage);
            roundEndSummaryContainer.AddChild(gamemodeLabel);

            //Duration
            var roundTimeLabel = new RichTextLabel();
            roundTimeLabel.SetMarkup(Loc.GetString("round-end-summary-window-duration-label",
                                                   ("hours", roundDuration.Hours),
                                                   ("minutes", roundDuration.Minutes),
                                                   ("seconds", roundDuration.Seconds)));
            roundEndSummaryContainer.AddChild(roundTimeLabel);

            //Round end text
            if (!string.IsNullOrEmpty(roundEnd))
            {
                var roundEndLabel = new RichTextLabel();
                roundEndLabel.SetMarkup(roundEnd);
                roundEndSummaryContainer.AddChild(roundEndLabel);
            }

            roundEndSummaryContainerScrollbox.AddChild(roundEndSummaryContainer);
            roundEndSummaryTab.AddChild(roundEndSummaryContainerScrollbox);

            return roundEndSummaryTab;
        }

        private BoxContainer MakePlayerManifestTab(RoundEndMessageEvent.RoundEndPlayerInfo[] playersInfo)
        {
            var playerManifestTab = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Name = Loc.GetString("round-end-summary-window-player-manifest-tab-title")
            };

            var playerInfoContainerScrollbox = new ScrollContainer
            {
                VerticalExpand = true,
                Margin = new Thickness(10)
            };
            var playerInfoContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical
            };

            //Put observers at the bottom of the list. Put antags on top.
            var sortedPlayersInfo = playersInfo.OrderBy(p => p.Observer).ThenBy(p => !p.Antag);

            //Create labels for each player info.
            foreach (var playerInfo in sortedPlayersInfo)
            {
                var hBox = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                };

                var playerInfoText = new RichTextLabel
                {
                    VerticalAlignment = VAlignment.Center,
                    VerticalExpand = true,
                };

                if (playerInfo.PlayerNetEntity != null)
                {
                    hBox.AddChild(new SpriteView(playerInfo.PlayerNetEntity.Value, _entityManager)
                        {
                            OverrideDirection = Direction.South,
                            VerticalAlignment = VAlignment.Center,
                            SetSize = new Vector2(32, 32),
                            VerticalExpand = true,
                        });
                }

                if (playerInfo.PlayerICName != null)
                {
                    if (playerInfo.Observer)
                    {
                        playerInfoText.SetMarkup(
                            Loc.GetString("round-end-summary-window-player-info-if-observer-text",
                                          ("playerOOCName", playerInfo.PlayerOOCName),
                                          ("playerICName", playerInfo.PlayerICName)));
                    }
                    else
                    {
                        //TODO: On Hover display a popup detailing more play info.
                        //For example: their antag goals and if they completed them sucessfully.
                        var icNameColor = playerInfo.Antag ? "red" : "white";
                        playerInfoText.SetMarkup(
                            Loc.GetString("round-end-summary-window-player-info-if-not-observer-text",
                                ("playerOOCName", playerInfo.PlayerOOCName),
                                ("icNameColor", icNameColor),
                                ("playerICName", playerInfo.PlayerICName),
                                ("playerRole", Loc.GetString(playerInfo.Role))));
                    }
                }
                hBox.AddChild(playerInfoText);
                playerInfoContainer.AddChild(hBox);
            }

            playerInfoContainerScrollbox.AddChild(playerInfoContainer);
            playerManifestTab.AddChild(playerInfoContainerScrollbox);

            return playerManifestTab;
        }
    }

}
