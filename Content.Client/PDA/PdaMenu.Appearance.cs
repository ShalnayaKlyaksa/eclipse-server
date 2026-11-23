using System.IO;
using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Client.PDA;

public sealed partial class PdaMenu
{
    private OwnedTexture? _personalImage;
    private bool _imageDialogOpen;
    private Label? _imageFeedback;
    private bool _appearanceBuilt;
    private bool _appearanceDirty;
    private ResPath ImagePath => new("/pda-desktop-" + IoCManager.Resolve<IPlayerManager>().LocalSession?.UserId.UserId + ".png");

    private void OpenAppearance()
    {
        if (!_appearanceBuilt)
            BuildAppearance();

        ShowPage(AppearanceView, Loc.GetString("pda-os-personalize"));
    }

    private void BuildAppearance()
    {
        _appearanceBuilt = true;
        AppearancePage.RemoveAllChildren();
        _imageFeedback = null;
        if (!_profileReady)
        {
            AppearancePage.AddChild(new Label { Text = Loc.GetString("pda-os-loading"), StyleClasses = { "terminal-muted" } });
            return;
        }

        var studio = _profile.Owned.Contains("studio");
        var wallpaper = _profile.Wallpaper;
        var custom = new CheckBox { Text = Loc.GetString("pda-os-custom-wallpaper"), Pressed = _profile.CustomWallpaper };
        var wallpapers = new BoxContainer { SeparationOverride = 6 };
        var wallpaperButtons = new ButtonGroup();
        ContainerButton? selectedWallpaper = null;
        foreach (var id in _profile.Owned.Where(id => id != "studio"))
        {
            var select = new ContainerButton
            {
                StyleClasses = { "button" },
                ToggleMode = true,
                Group = wallpaperButtons,
                Pressed = wallpaper == id && !custom.Pressed,
                HorizontalExpand = true,
                Children =
                {
                    new BoxContainer
                    {
                        Orientation = BoxContainer.LayoutOrientation.Vertical,
                        SeparationOverride = 3,
                        Children =
                        {
                            new PdaWallpaperPreview(id) { MinHeight = 64 },
                            new Label { Text = Loc.GetString("pda-os-wallpaper-" + id), Align = Label.AlignMode.Center },
                        },
                    },
                },
            };
            if (wallpaper == id)
                selectedWallpaper = select;
            select.OnPressed += _ =>
            {
                wallpaper = id;
                selectedWallpaper = select;
                custom.Pressed = false;
                _appearanceDirty = true;
            };
            wallpapers.AddChild(select);
        }
        custom.OnToggled += args =>
        {
            if (selectedWallpaper != null)
                selectedWallpaper.Pressed = !args.Pressed;
        };
        AddAppearanceSection("pda-os-settings-wallpaper", wallpapers);

        var card = new CheckBox { Text = Loc.GetString("pda-os-widget-card"), Pressed = _profile.Card };
        var text = new LineEdit { Text = _profile.CardText, PlaceHolder = Loc.GetString("pda-os-card-placeholder") };
        var decor = new CheckBox { Text = Loc.GetString("pda-os-widget-decor"), Pressed = _profile.Decor };
        var photo = new CheckBox { Text = Loc.GetString("pda-os-widget-photo"), Pressed = _profile.Photo };
        foreach (var option in new[] { card, decor, photo, custom })
            option.OnToggled += _ => _appearanceDirty = true;
        text.OnTextChanged += _ => _appearanceDirty = true;

        if (studio)
        {
            var upload = PdaAppUi.Action(Loc.GetString("pda-os-load-image"));
            upload.HorizontalAlignment = HAlignment.Left;
            upload.OnPressed += _ => LoadImage();
            _imageFeedback = new Label { Text = Loc.GetString("pda-os-image-local"), StyleClasses = { "terminal-muted" } };
            var widgets = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4,
                Children = { card, text, decor },
            };
            var images = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 4,
                Children = { upload, custom, photo },
            };
            AddAppearanceSection("pda-os-wallpaper-studio", new BoxContainer { SeparationOverride = 12, Children = { widgets, images } }, _imageFeedback);
        }
        else
        {
            AddAppearanceSection("pda-os-wallpaper-studio", new Label { Text = Loc.GetString("pda-os-studio-locked"), StyleClasses = { "terminal-muted" } });
        }

        var save = PdaAppUi.Action(Loc.GetString("pda-os-save-desktop"), primary: true);
        save.HorizontalAlignment = HAlignment.Right;
        save.OnPressed += _ =>
        {
            _profile.Wallpaper = wallpaper;
            _profile.Card = card.Pressed;
            _profile.Decor = decor.Pressed;
            _profile.Photo = photo.Pressed;
            _profile.CustomWallpaper = custom.Pressed;
            _profile.CardText = text.Text[..Math.Min(256, text.Text.Length)];
            _appearanceDirty = false;
            SaveDesktop();
        };
        AppearancePage.AddChild(save);
    }

    private void AddAppearanceSection(string title, params Control[] fields)
    {
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10, 8),
            SeparationOverride = 5,
            Children = { new Label { Text = Loc.GetString(title), StyleClasses = { "pda-caption" } } },
        };
        foreach (var field in fields)
            column.AddChild(field);
        AppearancePage.AddChild(new PanelContainer { StyleClasses = { "pda-editor" }, Children = { column } });
    }

    private void LoadPersonalImage()
    {
        if (_personalImage != null || !_profile.Owned.Contains("studio")) return;
        var resources = IoCManager.Resolve<IResourceManager>();
        try
        {
            if (!resources.UserData.Exists(ImagePath)) return;
            using var stream = resources.UserData.OpenRead(ImagePath);
            _personalImage = IoCManager.Resolve<IClyde>().LoadTextureFromPNGStream(stream, "PDA personal desktop");
            Desktop.PersonalImage = _personalImage;
        }
        catch (Exception)
        {
            // A missing or damaged local image must never prevent opening the PDA.
            if (_imageFeedback != null) _imageFeedback.Text = Loc.GetString("pda-os-image-error");
        }
    }

    private async void LoadImage()
    {
        if (_imageDialogOpen || !_profile.Owned.Contains("studio")) return;
        _imageDialogOpen = true;
        try
        {
            await using var file = await IoCManager.Resolve<IFileDialogManager>().OpenFile(new FileDialogFilters(new FileDialogFilters.Group("png", "jpg", "jpeg")), FileAccess.Read);
            if (file == null || Disposed) return;
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await file.ReadAsync(buffer)) > 0)
            {
                if (memory.Length + count > 8 * 1024 * 1024) throw new InvalidDataException();
                memory.Write(buffer, 0, count);
            }
            if (Disposed) return;
            memory.Position = 0;
            using var image = Image.Load<Rgba32>(memory);
            if (image.Width > 4096 || image.Height > 4096) throw new InvalidDataException();
            var scale = Math.Min(1f, Math.Min(1600f / image.Width, 1000f / image.Height));
            // Clone/Resize are supported by the content sandbox; Mutate/ResizeOptions are not.
            using var resized = image.Clone(context => context.Resize(Math.Max(1, (int) (image.Width * scale)), Math.Max(1, (int) (image.Height * scale))));
            var resources = IoCManager.Resolve<IResourceManager>();
            using (var output = resources.UserData.OpenWrite(ImagePath)) resized.SaveAsPng(output);
            var texture = IoCManager.Resolve<IClyde>().LoadTextureFromImage(resized, "PDA personal desktop");
            _personalImage?.Dispose();
            _personalImage = texture;
            Desktop.PersonalImage = texture;
            if (_imageFeedback != null && !_imageFeedback.Disposed) _imageFeedback.Text = Loc.GetString("pda-os-image-loaded");
        }
        catch (Exception)
        {
            if (_imageFeedback != null && !_imageFeedback.Disposed) _imageFeedback.Text = Loc.GetString("pda-os-image-error");
        }
        finally { _imageDialogOpen = false; }
    }
}

