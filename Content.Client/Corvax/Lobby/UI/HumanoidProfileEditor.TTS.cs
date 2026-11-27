using System.Linq;
using Content.Client.Corvax.TTS;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Humanoid;
using Robust.Client.UserInterface;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private TTSTab? _ttsTab;

    private void RefreshVoiceTab()
    {
        if (!_cfgManager.GetCVar(CCCVars.TTSEnabled))
            return;

        _ttsTab = new TTSTab();
        var children = new List<Control>();
        foreach (var child in TabContainer.Children)
            children.Add(child);

        TabContainer.RemoveAllChildren();

        for (int i = 0; i < children.Count; i++)
        {
            if (i == 1) // Set the tab to the 2nd place.
            {
                TabContainer.AddChild(_ttsTab);
            }
            TabContainer.AddChild(children[i]);
        }

        TabContainer.SetTabTitle(1, Loc.GetString("humanoid-profile-editor-voice-tab"));
        TtsVoiceRow.Visible = true;
        TtsVoiceButton.OnPressed += _ => TabContainer.CurrentTab = _ttsTab.GetPositionInParent();
        TtsPreviewButton.OnPressed += _ =>
        {
            if (Profile != null)
                _entManager.System<TTSSystem>().RequestPreviewTTS(Profile.Voice);
        };

        _ttsTab.OnVoiceSelected += voiceId =>
        {
            SetVoice(voiceId);
        };

        _ttsTab.OnPreviewRequested += voiceId =>
        {
            _entManager.System<TTSSystem>().RequestPreviewTTS(voiceId);
        };
    }

    private void UpdateTTSVoicesControls()
    {
        if (Profile is null || _ttsTab is null)
            return;

        var voices = _preferencesManager.Settings?.TtsVoices ?? [];
        if (!voices.Any(voice => voice.Id == Profile.Voice && voice.Selectable))
        {
            var fallback = voices.FirstOrDefault(voice => voice.Selectable && voice.Id == HumanoidProfileSystem.DefaultVoice);
            if (fallback.Id is null)
                fallback = voices.FirstOrDefault(voice => voice.Selectable);

            if (fallback.Id is not null)
                Profile = Profile.WithVoice(fallback.Id);
        }

        _ttsTab.UpdateControls(Profile.Voice, voices);
        UpdateTtsVoiceLabel();
    }

    private void SetVoice(string newVoice)
    {
        Profile = Profile?.WithVoice(newVoice);
        _ttsTab?.SetSelectedVoice(newVoice);
        UpdateTtsVoiceLabel();
        SetDirty();
    }

    private void UpdateTtsVoiceLabel()
    {
        var voices = _preferencesManager.Settings?.TtsVoices ?? [];
        var voice = voices.FirstOrDefault(voice => voice.Id == Profile?.Voice && voice.Selectable);
        TtsVoiceButton.Text = voice.Id is null
            ? Loc.GetString("humanoid-profile-editor-voice-none")
            : _ttsTab!.GetVoiceName(voice);
        TtsVoiceButton.Disabled = voices.Length == 0;
        TtsPreviewButton.Disabled = voice.Id is null;
    }
}
