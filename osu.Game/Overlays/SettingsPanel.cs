﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osuTK;
using osuTK.Graphics;
using osu.Framework.Allocation;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;

namespace osu.Game.Overlays
{
    public abstract class SettingsPanel : OsuFocusedOverlayContainer
    {
        public const float CONTENT_MARGINS = 15;

        public const float TRANSITION_LENGTH = 600;

        private const float sidebar_width = Sidebar.DEFAULT_WIDTH;

        /// <summary>
        /// The width of the settings panel content, excluding the sidebar.
        /// </summary>
        public const float PANEL_WIDTH = 400;

        /// <summary>
        /// The full width of the settings panel, including the sidebar.
        /// </summary>
        public const float WIDTH = sidebar_width + PANEL_WIDTH;

        protected Container<Drawable> ContentContainer;

        protected override Container<Drawable> Content => ContentContainer;

        protected Sidebar Sidebar;
        private SidebarButton selectedSidebarButton;

        protected SettingsSectionsContainer SectionsContainer;

        private SeekLimitedSearchTextBox searchTextBox;

        protected override string PopInSampleName => "UI/settings-pop-in";

        /// <summary>
        /// Provide a source for the toolbar height.
        /// </summary>
        public Func<float> GetToolbarHeight;

        private readonly bool showSidebar;

        private LoadingLayer loading;

        private readonly List<SettingsSection> loadableSections = new List<SettingsSection>();

        private Task sectionsLoadingTask;

        protected SettingsPanel(bool showSidebar)
        {
            this.showSidebar = showSidebar;
            RelativeSizeAxes = Axes.Y;
            AutoSizeAxes = Axes.X;
        }

        protected virtual IEnumerable<SettingsSection> CreateSections() => null;

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = ContentContainer = new NonMaskedContent
            {
                X = -WIDTH + ExpandedPosition,
                Width = PANEL_WIDTH,
                RelativeSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Scale = new Vector2(2, 1), // over-extend to the left for transitions
                        RelativeSizeAxes = Axes.Both,
                        Colour = OsuColour.Gray(0.05f),
                        Alpha = 1,
                    },
                    loading = new LoadingLayer
                    {
                        State = { Value = Visibility.Visible }
                    }
                }
            };

            Add(SectionsContainer = new SettingsSectionsContainer
            {
                Masking = true,
                RelativeSizeAxes = Axes.Both,
                ExpandableHeader = CreateHeader(),
                FixedHeader = searchTextBox = new SeekLimitedSearchTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    Origin = Anchor.TopCentre,
                    Anchor = Anchor.TopCentre,
                    Width = 0.95f,
                    Margin = new MarginPadding
                    {
                        Top = 20,
                        Bottom = 20
                    },
                },
                Footer = CreateFooter().With(f => f.Alpha = 0)
            });

            if (showSidebar)
            {
                AddInternal(Sidebar = new Sidebar { Width = sidebar_width });
            }

            CreateSections()?.ForEach(AddSection);
        }

        protected void AddSection(SettingsSection section)
        {
            if (IsLoaded)
                // just to keep things simple. can be accommodated for if we ever need it.
                throw new InvalidOperationException("All sections must be added before the panel is loaded.");

            loadableSections.Add(section);
        }

        protected virtual Drawable CreateHeader() => new Container();

        protected virtual Drawable CreateFooter() => new Container();

        protected override void PopIn()
        {
            base.PopIn();

            ContentContainer.MoveToX(ExpandedPosition, TRANSITION_LENGTH, Easing.OutQuint);

            // delay load enough to ensure it doesn't overlap with the initial animation.
            // this is done as there is still a brief stutter during load completion which is more visible if the transition is in progress.
            // the eventual goal would be to remove the need for this by splitting up load into smaller work pieces, or fixing the remaining
            // load complete overheads.
            Scheduler.AddDelayed(loadSections, TRANSITION_LENGTH / 3);

            Sidebar?.MoveToX(0, TRANSITION_LENGTH, Easing.OutQuint);
            this.FadeTo(1, TRANSITION_LENGTH, Easing.OutQuint);

            searchTextBox.HoldFocus = true;
        }

        protected virtual float ExpandedPosition => 0;

        protected override void PopOut()
        {
            base.PopOut();

            ContentContainer.MoveToX(-WIDTH + ExpandedPosition, TRANSITION_LENGTH, Easing.OutQuint);

            Sidebar?.MoveToX(-sidebar_width, TRANSITION_LENGTH, Easing.OutQuint);
            this.FadeTo(0, TRANSITION_LENGTH, Easing.OutQuint);

            searchTextBox.HoldFocus = false;
            if (searchTextBox.HasFocus)
                GetContainingInputManager().ChangeFocus(null);
        }

        public override bool AcceptsFocus => true;

        protected override void OnFocus(FocusEvent e)
        {
            searchTextBox.TakeFocus();
            base.OnFocus(e);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            ContentContainer.Margin = new MarginPadding { Left = Sidebar?.DrawWidth ?? 0 };
            Padding = new MarginPadding { Top = GetToolbarHeight?.Invoke() ?? 0 };
        }

        private const double fade_in_duration = 1000;

        private void loadSections()
        {
            if (sectionsLoadingTask != null)
                return;

            sectionsLoadingTask = LoadComponentsAsync(loadableSections, sections =>
            {
                SectionsContainer.AddRange(sections);
                SectionsContainer.Footer.FadeInFromZero(fade_in_duration, Easing.OutQuint);
                SectionsContainer.SearchContainer.FadeInFromZero(fade_in_duration, Easing.OutQuint);

                loading.Hide();

                searchTextBox.Current.BindValueChanged(term => SectionsContainer.SearchContainer.SearchTerm = term.NewValue, true);
                searchTextBox.TakeFocus();

                loadSidebarButtons();
            });
        }

        private void loadSidebarButtons()
        {
            if (Sidebar == null)
                return;

            LoadComponentsAsync(createSidebarButtons(), buttons =>
            {
                float delay = 0;

                foreach (var button in buttons)
                {
                    Sidebar.Add(button);

                    button.FadeOut()
                          .Delay(delay)
                          .FadeInFromZero(fade_in_duration, Easing.OutQuint);

                    delay += 40;
                }

                SectionsContainer.SelectedSection.BindValueChanged(section =>
                {
                    if (selectedSidebarButton != null)
                        selectedSidebarButton.Selected = false;

                    selectedSidebarButton = Sidebar.Children.Single(b => b.Section == section.NewValue);
                    selectedSidebarButton.Selected = true;
                }, true);
            });
        }

        private IEnumerable<SidebarButton> createSidebarButtons()
        {
            foreach (var section in SectionsContainer)
            {
                yield return new SidebarButton
                {
                    Section = section,
                    Action = () =>
                    {
                        if (!SectionsContainer.IsLoaded)
                            return;

                        SectionsContainer.ScrollTo(section);
                        Sidebar.State = ExpandedState.Contracted;
                    },
                };
            }
        }

        private class NonMaskedContent : Container<Drawable>
        {
            // masking breaks the pan-out transform with nested sub-settings panels.
            protected override bool ComputeIsMaskedAway(RectangleF maskingBounds) => false;
        }

        public class SettingsSectionsContainer : SectionsContainer<SettingsSection>
        {
            public SearchContainer<SettingsSection> SearchContainer;

            protected override FlowContainer<SettingsSection> CreateScrollContentContainer()
                => SearchContainer = new SearchContainer<SettingsSection>
                {
                    AutoSizeAxes = Axes.Y,
                    RelativeSizeAxes = Axes.X,
                    Direction = FillDirection.Vertical,
                };

            public SettingsSectionsContainer()
            {
                HeaderBackground = new Box
                {
                    Colour = Color4.Black,
                    RelativeSizeAxes = Axes.Both
                };
            }

            protected override void UpdateAfterChildren()
            {
                base.UpdateAfterChildren();

                // no null check because the usage of this class is strict
                HeaderBackground.Alpha = -ExpandableHeader.Y / ExpandableHeader.LayoutSize.Y;
            }
        }
    }
}
