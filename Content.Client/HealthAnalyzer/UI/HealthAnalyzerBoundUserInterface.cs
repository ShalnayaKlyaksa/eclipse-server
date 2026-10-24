using Content.Client._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.MedicalScanner;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;

namespace Content.Client.HealthAnalyzer.UI
{
    [UsedImplicitly]
    public sealed class HealthAnalyzerBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private HealthAnalyzerWindow? _window;

        // For advanced-health patients the scanner opens the neurochip status menu instead.
        private AdvancedHealthStatusWindow? _advancedWindow;
        private EntityUid? _lastPatient;
        // Set when the medic manually closes the menu, so periodic scan updates don't re-open it.
        // Cleared on a new patient or when the scanner re-acquires the target (out-of-range -> in-range).
        private bool _dismissed;
        private bool _lastScanActive;

        public HealthAnalyzerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();
        }

        protected override void ReceiveMessage(BoundUserInterfaceMessage message)
        {
            // A deliberate (re)scan clears a prior manual dismissal so the menu re-opens; the state
            // message that follows immediately does the actual opening.
            if (message is HealthAnalyzerScanStartedMessage)
            {
                _dismissed = false;
                _lastScanActive = false;
                return;
            }

            if (message is not HealthAnalyzerScannedUserMessage cast)
                return;

            // The server marks advanced-health patients by filling in the advanced fields; this is
            // authoritative and avoids client-side replication timing issues.
            var isAdvanced = cast.State.AdvancedBodyParts != null || cast.State.AdvancedBloodVolume != null;

            if (isAdvanced && cast.State.TargetEntity is { } net && EntMan.TryGetEntity(net, out var patient))
            {
                // Server always sets this for a real scan message; default a missing value to active
                // so we never spuriously close.
                var scanActive = cast.State.ScanMode ?? true;

                // New target: forget any previous dismissal.
                if (_lastPatient != patient.Value)
                {
                    _dismissed = false;
                    _advancedWindow?.Close();
                    _advancedWindow = null;
                }

                // A fresh acquisition (out-of-range/off -> active) counts as a new scan and clears a
                // previous manual dismissal, so walking away and back (or re-scanning) re-opens it.
                if (scanActive && !_lastScanActive)
                    _dismissed = false;

                _lastPatient = patient.Value;
                _lastScanActive = scanActive;

                // Scanner lost the target (out of range / turned off): auto-close the menu.
                if (!scanActive)
                {
                    _advancedWindow?.Close();
                    return;
                }

                // Open only when not manually dismissed and not already showing.
                if (!_dismissed && _advancedWindow is not { IsOpen: true })
                {
                    var medic = IoCManager.Resolve<IPlayerManager>().LocalEntity ?? patient.Value;
                    var window = new AdvancedHealthStatusWindow(patient.Value, medic);
                    // Only a close of the *current* window counts as a dismissal — a deferred close of
                    // a superseded window (patient switch) must not flip the flag for the new one.
                    window.OnClose += () =>
                    {
                        if (_advancedWindow == window)
                            _dismissed = true;
                    };
                    _advancedWindow = window;
                    window.Open();
                }
                return;
            }

            _window ??= this.CreateWindow<HealthAnalyzerWindow>();
            _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
            _window.Populate(cast);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            _window?.Dispose();
            _advancedWindow?.Close();
            _advancedWindow = null;
            _lastPatient = null;
            _dismissed = false;
            _lastScanActive = false;
        }
    }
}
