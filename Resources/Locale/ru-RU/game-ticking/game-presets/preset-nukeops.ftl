nukeops-title = Легионеры Пепла
nukeops-description = Легион пепла нацелился на станцию. Постарайтесь не дать им взвести и взорвать ядерную бомбу, защищая ядерный диск!

nukeops-welcome =
    Вы — оперативник Легиона пепла. Ваша задача — взорвать { $station } и убедиться, что от неё осталась лишь груда обломков. Ваше руководство, Легион, снабдило вас всем необходимым для выполнения этой задачи.
    Операция "{ $name }" началась! Смерть Nanotrasen!
nukeops-briefing = Ваши задачи просты. Доставить бомбу и убраться до того, как она взорвётся. Начинайте миссию.

nukeops-opsmajor = [color=crimson]Крупная победа Легиона![/color]
nukeops-opsminor = [color=crimson]Малая победа Легиона![/color]
nukeops-neutral = [color=yellow]Ничейный исход![/color]
nukeops-crewminor = [color=green]Малая победа экипажа![/color]
nukeops-crewmajor = [color=green]Разгромная победа экипажа![/color]

nukeops-cond-nukeexplodedoncorrectstation = Легиону пепла удалось взорвать станцию.
nukeops-cond-nukeexplodedonnukieoutpost = Аванпост Легиона пепла был уничтожен ядерным взрывом!
nukeops-cond-nukeexplodedonincorrectlocation = Ядерная бомба взорвалась вне станции.
nukeops-cond-nukeactiveinstation = Ядерная бомба была оставлена взведённой на станции.
nukeops-cond-nukeactiveatcentcom = Ядерная бомба была доставлев Эйдосской империи!
nukeops-cond-nukediskoncentcom = Экипаж улетел с диском ядерной аутентификации.
nukeops-cond-nukedisknotoncentcom = Экипаж оставил диск ядерной аутентификации на станции.
nukeops-cond-nukiesabandoned = Легион пепла были брошены.
nukeops-cond-allnukiesdead = Все члены Легиона пепла погибли.
nukeops-cond-somenukiesalive = Несколько членов Легиона пепла погибли.
nukeops-cond-allnukiesalive = Все Легион пепла выжили.

nukeops-disk-location-title = Конечное местоположение диска:
nukeops-disk-carried-by = {" "}у [color=White]{$name}[/color], [color=orange]{$job}[/color], {$location} { $user ->
    [unknown] { "" }
    *[other] ([color=gray]{$user}[/color])
}

storage-hierarchy-list = { $items-left ->
  [0] { $existing-text } { $item },
  *[other] { $existing-text } { $item }, в
}

nukeops-list-start = Оперативниками были:
nukeops-list-name = - [color=White]{ $name }[/color]
nukeops-list-name-user = - [color=White]{ $name }[/color] ([color=gray]{ $user }[/color])
nukeops-not-enough-ready-players = Недостаточно игроков готовы к игре! { $readyPlayersCount } игроков из необходимых { $minimumPlayers } готовы. Нельзя запустить пресет «Легион пепла».
nukeops-no-one-ready = Нет готовых игроков! Нельзя запустить пресет «Легион пепла».

nukeops-role-commander = Командир
nukeops-role-agent = Медик
nukeops-role-operator = Оператор
