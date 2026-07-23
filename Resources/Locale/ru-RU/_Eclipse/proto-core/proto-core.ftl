ent-ProtoCore = Протоядро
    .desc = Стационарное имперское хранилище энергии. Используется для поддержания аварийных цепей питания и резервных контуров станции.
ent-ProtoCoreActivationKey = ключ активации
    .desc = Защищенный имперский ключ данных, предоставляющий доступ к аварийным протоколам Протоядра.
ent-ProtoCoreZeroShiftActivationKey = ключ активации нулевой смены
    .desc = Имперский ключ принудительного запуска, переводящий Протоядро в режим плавления без протоколов эвакуации.
ent-AshHackingDevice = пепельный взламывающий аппарат
    .desc = Компактный взламывающий аппарат для внешней авторизации в системе Протоядра.
ent-ProtoCoreSMES = СМЭС Протоядра
    .desc = Защищённый сверхпроводниковый накопитель, настроенный для стабилизации Протоядра.
ent-PinpointerProtoCoreActivationKey = трекер ключа активации
    .desc = Ручной трекер, настроенный на аварийный маячок ключа активации.
ent-PinpointerSyndicateNuclear = пинпоинтер Легиона Пепла
    .desc = Изготовлен специально для миссий Легиона Пепла, найдите ключ активации!
ent-ActivationKeyTrackerCartridge = картридж поиска ключа активации
    .desc = Одноразовый картридж с приложением для отслеживания ключа активации Протоядра.
ent-AshLegionActivationKeyTrackerCartridge = { ent-ActivationKeyTrackerCartridge }
    .desc = { ent-ActivationKeyTrackerCartridge.desc }

ash-legion-title = Легионеры Пепла
ash-legion-description = Легион Пепла является повстанческой группировкой, выступающей против устоев империи, целью на станции является уничтожение объекта вне зависимости от способа, будь то взрыв ядерного реактора, создание чёрной дыры, или плавление протоядра.
ash-legion-welcome =
    Вы - Легион Пепла.
    Проникните на {$station}, проанализируйте ситуацию, и выберите лучший способ уничтожения станции
ash-legion-briefing = Захватите ключ активации, взломайте консоль Протоядра, начните плавление и удерживайте ядро до завершения отсчета.

roles-antag-ash-legion-name = оперативник Легиона Пепла
roles-antag-ash-legion-medic-name = медик Легиона Пепла
roles-antag-ash-legion-commander-name = командир Легиона Пепла
roles-antag-ash-legion-objective = Используйте ключ активации и пепельный взламывающий аппарат, чтобы запустить процедуру плавления Протоядра.

proto-core-announcement-sender = Эйдосская империя
proto-core-announcement-unauthorized-connection = ПОДТВЕРЖДЁН ПРОЛОМ БЕЗОПАСНОСТИ. Гарантийная печать Протоядра сорвана. Внешняя консоль управления недоступна.
proto-core-announcement-meltdown-started = ПРОТОКОЛ MD-111 ВВЕДЁН ПРИНУДИТЕЛЬНО. Протоядро переведено в режим плавления. Полный распад через {$time}.
proto-core-announcement-stabilized = Стабилизация завершена. Протоядро возвращено в режим стационарной работы.
proto-core-announcement-critical = На объекте введён протокол VE-062, Сдерживание плавления более невозможно. Производится принудительная эвакуация. Эвакуационный шаттл прибудет через 1 минуту.
proto-core-announcement-critical-no-evac = На объекте введён протокол VL-085: Окончательное плавление топливных стержней более невозможно. Контракты расторгнуты, ваша жизнь теперь в ваших руках. И помните - Империя заботится о вас!
proto-core-announcement-shuttle-called = Эвакуационный шаттл вызван из-за критической ситуации. Прибытие через 1 минуту.
proto-core-announcement-storage-disconnected = Контур стабилизационных СМЭСов Протоядра был разорван при наличии заряда. Сдерживание ускорено до критического состояния.

proto-core-console-key-accepted = Ключ активации авторизует аварийный канал Протоядра.
proto-core-console-hack-start = Пепельный взламывающий аппарат начинает принудительное подключение.
proto-core-console-device-install-start = Установка взламывающего аппарата в терминал Протоядра...
proto-core-console-device-already-connected = Взламывающий аппарат уже заблокирован в контуре Протоядра.
proto-core-console-device-disconnected = Взламывающий аппарат выведен из контура Протоядра.
proto-core-console-storage-charged = Стабилизация отклонена. Подключённые СМЭСы Протоядра всё ещё содержат заряд.
ash-hacking-device-locked = Аппарат заблокирован в контуре Протоядра. Отключение возможно только через консоль управления.

proto-core-verb-start = Запустить процедуру плавления
proto-core-verb-stabilize = Стабилизировать Протоядро
proto-core-verb-defuse = Обезвредить Протоядро
proto-core-verb-physical-override = Принудительно отключить аппарат

proto-core-state-idle = ожидание
proto-core-state-hacked = внешнее подключение
proto-core-state-meltdown = плавление
proto-core-state-critical = критическое состояние
proto-core-state-stabilized = стабилизировано
proto-core-examine-status = Состояние: {$state}. Оставшееся время: {$time}.
proto-core-console-examine = Авторизация: {$authorized}. Взламывающий аппарат: {$device}.
proto-core-slot-component-slot-name-key = ключ активации
proto-core-slot-component-slot-name-device = взламывающий аппарат
proto-core-ui-title = Терминал Протоядра
proto-core-ui-state = Состояние ядра
proto-core-ui-time = Оставшееся время
proto-core-ui-power-output = Выработка ядра
proto-core-ui-stored-energy = Энергия в сети ядра
proto-core-ui-key-insert = Вставить ключ активации
proto-core-ui-key-eject = Извлечь ключ активации
proto-core-ui-device-insert = Вставить взламывающий аппарат
proto-core-ui-device-eject = Извлечь взламывающий аппарат

ash-legion-roundend-critical = Протоядро достигло критического состояния.
ash-legion-roundend-stabilized = Протоядро было стабилизировано.
ash-legion-roundend-started = Легион Пепла активировал процедуру плавления Протоядра.
steal-target-groups-proto-core-activation-key = ключ активации
