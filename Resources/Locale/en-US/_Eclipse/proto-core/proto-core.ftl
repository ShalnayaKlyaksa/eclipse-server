ent-ProtoCore = proto core
    .desc = A stationary imperial energy vault used for emergency station power reserves.
ent-ProtoCoreActivationKey = activation key
    .desc = A secured imperial data key that grants access to emergency proto core protocols.
ent-ProtoCoreZeroShiftActivationKey = zero shift activation key
    .desc = An imperial override key that forces proto core meltdown without evacuation protocols.
ent-AshHackingDevice = ash hacking device
    .desc = A compact breaching apparatus built to force external authorization into a proto core.
ent-ProtoCoreSMES = proto core SMES
    .desc = A protected superconducting storage unit tuned for proto core stabilization.
ent-PinpointerProtoCoreActivationKey = activation key tracker
    .desc = A handheld tracker locked onto the activation key's emergency beacon.
ent-PinpointerSyndicateNuclear = ash legion pinpointer
    .desc = Produced specifically for ash legion operative missions, get that key!
ent-ActivationKeyTrackerCartridge = activation key tracker cartridge
    .desc = A single-use cartridge containing an application that tracks the proto core activation key.
ent-AshLegionActivationKeyTrackerCartridge = { ent-ActivationKeyTrackerCartridge }
    .desc = { ent-ActivationKeyTrackerCartridge.desc }

ash-legion-title = Ash Legionnaires
ash-legion-description = The Ash Legion targets the station's built-in proto core instead of bringing a nuclear device.
ash-legion-welcome =
    You are the Ash Legion.
    Infiltrate {$station}, seize the activation key, connect the ash hacking device to the proto core console, and start the core meltdown procedure.
ash-legion-briefing = Seize the activation key, breach the proto core console, begin meltdown, and hold the core until the countdown completes.

roles-antag-ash-legion-name = Ash Legion operative
roles-antag-ash-legion-medic-name = Ash Legion medic
roles-antag-ash-legion-commander-name = Ash Legion commander
roles-antag-ash-legion-objective = Use the activation key and ash hacking device to start the proto core meltdown procedure.

proto-core-announcement-sender = Eidos Empire
proto-core-announcement-unauthorized-connection = SECURITY BREACH CONFIRMED. Proto core command seal broken. External control lock acquired.
proto-core-announcement-meltdown-started = DELTA PROTOCOL ENFORCED. Proto core switched to meltdown mode. Total collapse in {$time}.
proto-core-announcement-stabilized = Stabilization complete. The proto core has returned to emergency energy accumulation mode.
proto-core-announcement-critical = Eidos Imperial directive: containment failure is irreversible. Evacuate immediately. Emergency shuttle will arrive in 1 minute.
proto-core-announcement-critical-no-evac = Eidos Imperial directive: containment failure is irreversible. Evacuation protocols are disabled. Clear the immediate area.
proto-core-announcement-shuttle-called = Emergency shuttle called automatically due to proto core critical state. Arrival in 1 minute.
proto-core-announcement-storage-disconnected = Proto core stabilization storage was severed while charged. Containment failure accelerated.

proto-core-console-key-accepted = The activation key authorizes the proto core emergency channel.
proto-core-console-hack-start = The ash hacking device begins forcing a connection.
proto-core-console-device-install-start = Installing breaching device into the proto core terminal...
proto-core-console-device-already-connected = A breaching device is already locked into the proto core loop.
proto-core-console-device-disconnected = The breaching device is torn out of the proto core loop.
proto-core-console-storage-charged = Stabilization rejected. Connected proto core SMES units still contain charge.
ash-hacking-device-locked = The device is locked into the proto core loop. Disconnect it through the control console.

proto-core-verb-start = Start meltdown procedure
proto-core-verb-stabilize = Stabilize proto core
proto-core-verb-defuse = Defuse proto core
proto-core-verb-physical-override = Force device disconnect

proto-core-state-idle = idle
proto-core-state-hacked = externally connected
proto-core-state-meltdown = melting down
proto-core-state-critical = critical
proto-core-state-stabilized = stabilized
proto-core-examine-status = Status: {$state}. Remaining time: {$time}.
proto-core-console-examine = Authorization: {$authorized}. Breaching device: {$device}.
proto-core-slot-component-slot-name-key = activation key
proto-core-slot-component-slot-name-device = breaching device
proto-core-ui-title = Proto Core Terminal
proto-core-ui-state = Core state
proto-core-ui-time = Remaining time
proto-core-ui-power-output = Core output
proto-core-ui-stored-energy = Stored energy
proto-core-ui-key-insert = Insert activation key
proto-core-ui-key-eject = Eject activation key
proto-core-ui-device-insert = Insert breaching device
proto-core-ui-device-eject = Eject breaching device

ash-legion-roundend-critical = The proto core reached a critical state.
ash-legion-roundend-stabilized = The proto core was stabilized.
ash-legion-roundend-started = The Ash Legion activated the proto core meltdown procedure.
steal-target-groups-proto-core-activation-key = activation key

cmd-protocorearm-desc = Start the proto core meltdown timer. You can set timer directly. Uid is optional.
cmd-protocorearm-help = protocorearm <timer> <uid>
cmd-protocorearm-not-found = Can't find any entity with a ProtoCoreComponent.
cmd-protocorearm-1-help = Time (in seconds)
cmd-protocorearm-2-help = Proto core
