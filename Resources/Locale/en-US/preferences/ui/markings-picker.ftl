markings-search = Search
markings-filter-all = All
markings-filter-face = Face
markings-filter-eyes = Eyes
markings-filter-upper-ears = Top ears
markings-filter-side-ears = Side ears
markings-filter-horns = Horns
markings-filter-antennae = Antennae
markings-filter-hairstyles = Hairstyles
markings-filter-facial-hair = Facial hair
markings-filter-body-patterns = Body patterns
markings-filter-underwear = Underwear
markings-filter-tail-base = Tails and appendages
markings-filter-overlays = Overlays
markings-filter-left = Left side
markings-filter-right = Right side
markings-filter-hands = Hands
markings-filter-tattoos = Tattoos
markings-filter-scars = Scars
markings-filter-bandages = Bandages

-markings-selection = { $selectable ->
    [0] You have no markings remaining.
    [one] You can select one more marking.
   *[other] You can select { $selectable } more markings.
}
markings-limits = { $required ->
    [true] { $count ->
        [-1] Select at least one marking.
        [0] You cannot select any markings, but somehow, you have to? This is a bug.
        [one] Select one marking.
       *[other] Select at least one marking and up to {$count} markings. { -markings-selection(selectable: $selectable) }
    }
   *[false] { $count ->
        [-1] Select any number of markings.
        [0] You cannot select any markings.
        [one] Select up to one marking.
       *[other] Select up to {$count} markings. { -markings-selection(selectable: $selectable) }
    }
}
markings-reorder = Reorder markings

humanoid-marking-modifier-respect-limits = Respect limits
humanoid-marking-modifier-respect-group-sex = Respect group & sex restrictions
humanoid-marking-modifier-base-layers = Base layers
humanoid-marking-modifier-enable = Enable
humanoid-marking-modifier-prototype-id = Prototype id:

# Categories

markings-organ-Torso = Torso
markings-organ-Head = Head
markings-organ-ArmLeft = Left Arm
markings-organ-ArmRight = Right Arm
markings-organ-HandRight = Right Hand
markings-organ-HandLeft = Left Hand
markings-organ-LegLeft = Left Leg
markings-organ-LegRight = Right Leg
markings-organ-FootLeft = Left Foot
markings-organ-FootRight = Right Foot
markings-organ-Eyes = Eyes

markings-layer-Special = Special
markings-layer-Tail = Tail
markings-layer-Tail-Moth = Wings
markings-layer-Hair = Hair
markings-layer-FacialHair = Facial Hair
markings-layer-UndergarmentTop = Undershirt
markings-layer-UndergarmentBottom = Underpants
markings-layer-Chest = Chest
markings-layer-Head = Head
markings-layer-Snout = Snout
markings-layer-SnoutCover = Snout (Cover)
markings-layer-HeadSide = Head (Side)
markings-layer-HeadTop = Head (Top)
markings-layer-Eyes = Eyes
markings-layer-RArm = Right Arm
markings-layer-LArm = Left Arm
markings-layer-RHand = Right Hand
markings-layer-LHand = Left Hand
markings-layer-RLeg = Right Leg
markings-layer-LLeg = Left Leg
markings-layer-RFoot = Right Foot
markings-layer-LFoot = Left Foot
markings-layer-Overlay = Overlay
markings-layer-TailOverlay = Overlay
markings-layer-Wings = Wings

