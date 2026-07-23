advanced-health-part-auto = Авто
advanced-health-part-head = голова
advanced-health-part-neck = шея
advanced-health-part-chest = грудь
advanced-health-part-abdomen = живот
advanced-health-part-pelvis = таз
advanced-health-part-leftupperarm = левое плечо
advanced-health-part-leftforearm = левое предплечье
advanced-health-part-lefthand = левая кисть
advanced-health-part-rightupperarm = правое плечо
advanced-health-part-rightforearm = правое предплечье
advanced-health-part-righthand = правая кисть
advanced-health-part-leftthigh = левое бедро
advanced-health-part-leftshin = левая голень
advanced-health-part-leftfoot = левая стопа
advanced-health-part-rightthigh = правое бедро
advanced-health-part-rightshin = правая голень
advanced-health-part-rightfoot = правая стопа

advanced-health-target-verb = Прицел: {$part}
advanced-health-target-selected = Выбранная зона: {$part}.
advanced-health-target-tooltip = Штраф точности: {$penalty}. {$effect}
advanced-health-target-tooltip-auto = Взвешенный автоматический выбор зоны без штрафа точности.
advanced-health-target-effect-vital = Попадание может быстро лишить сознания или убить.
advanced-health-target-effect-internal = Риск повреждения органов и внутреннего кровотечения.
advanced-health-target-effect-arm = Может нарушить использование оружия и инструментов.
advanced-health-target-effect-leg = Может нарушить движение и устойчивость.
advanced-health-treatment-applied = Лечение применено к зоне «{$part}».
advanced-health-treatment-tourniquet-invalid = Жгут можно наложить только на конечность.
advanced-health-treatment-no-item = Нет подходящего медицинского предмета в руке.
advanced-health-treatment-too-far = Слишком далеко, чтобы оказать помощь.

advanced-health-scanner-title = Расширенная физиология
advanced-health-scanner-blood = Кровь: {$value}%
advanced-health-scanner-oxygen = Кислород: {$value}
advanced-health-scanner-pain = Боль: {$value}
advanced-health-scanner-shock = Шок: {$value}
advanced-health-scanner-trauma = Травматическая нагрузка: {$value}
advanced-health-scanner-no-bleeding = Активное кровотечение не обнаружено.
advanced-health-scanner-bleeding = Активное кровотечение: {$part}.

advanced-health-window-title = Состояние тела
advanced-health-window-vitals = Системное состояние
advanced-health-window-body-parts = Части тела
advanced-health-window-oxygenation = Оксигенация: {$value}%
advanced-health-window-shock = Шок: {$value}%
advanced-health-window-trauma = Травматическая нагрузка: {$value}
advanced-health-window-part-row = {$part}: {$severity} · ран: {$wounds} · {$statuses}
advanced-health-window-status-bleeding = кровотечение
advanced-health-window-status-bandaged = повязка
advanced-health-window-status-splinted = шина
advanced-health-window-status-tourniquet = жгут
advanced-health-window-status-destroyed = разрушена
advanced-health-severity-normal = норма
advanced-health-severity-minor = лёгкое
advanced-health-severity-moderate = среднее
advanced-health-severity-severe = тяжёлое
advanced-health-severity-critical = критическое

ent-AdvancedBandage = рулон бинта
    .desc = Стерильная марля с запасом перевязок. Обводите рану по кругу в окне здоровья.
ent-AdvancedPressureBandage = давящая повязка
    .desc = Тугий рулон бинта — эффективнее против сильного кровотечения. Расходует перевязку с рулона.
ent-AdvancedTourniquet = кровоостанавливающий жгут
    .desc = Почти полностью останавливает наружное кровотечение конечности.
ent-AdvancedSplint = медицинская шина
    .desc = Стабилизирует переломы и снижает вызванную ими боль.
ent-AdvancedHemostaticPowder = гемостатический порошок
    .desc = Временно снижает наружное кровотечение.
ent-AdvancedSutureKit = набор для швов
    .desc = Позволяет зашить открытые раны, останавливая кровотечение и снижая риск заражения.
ent-AdvancedForcepsPack = хирургический зажим
    .desc = Позволяет извлечь осколки и инородные тела из раны.
ent-BasicMedScanner = базовый медицинский сканер
    .desc = Показывает понятную сводку и локальные травмы пациентов с расширенным здоровьем.

# Full-screen self-diagnostic UI
advanced-health-ui-select-part = Выберите зону на теле
advanced-health-ui-no-wounds = Повреждения не обнаружены
advanced-health-ui-wounds-header = Состояние

advanced-health-cond-bleeding = Кровотечение
advanced-health-cond-foreign = Инородное тело
advanced-health-cond-fracture = Перелом
advanced-health-cond-treated = Обработано: {$list}
advanced-health-ui-tissue-header = Целостность тканей
advanced-health-ui-actions-header = Помощь

advanced-health-vital-blood = Кровь
advanced-health-vital-blood-value = {$liters} л · {$percent}%
advanced-health-vital-oxygen = Кислород
advanced-health-vital-consciousness = Сознание
advanced-health-vital-pain = Боль
advanced-health-vital-shock = Шок
advanced-health-vital-trauma = Травма
advanced-health-vital-temperature = Температура
advanced-health-vital-temperature-value = {$value}°C
advanced-health-vital-heart = Сердце
advanced-health-vital-heart-beating = бьётся
advanced-health-vital-heart-stopped = ОСТАНОВЛЕНО
advanced-health-vital-infection = Инфекция
advanced-health-vital-percent = {$value}%

# Fullscreen status menu
advanced-health-legend-healthy = Здорово
advanced-health-legend-minor = Лёгкие повреждения
advanced-health-legend-moderate = Умеренные повреждения
advanced-health-legend-severe = Тяжёлые повреждения
advanced-health-legend-critical = Критические повреждения
advanced-health-menu-liters = {$value} л
advanced-health-menu-liters-per-minute = {$value} л/м
advanced-health-menu-per-minute = {$value}/м
advanced-health-menu-rad = {$value}рад
advanced-health-menu-bp = {$sys} / {$dia} ({$pulse})
advanced-health-menu-o2 = {$value}% O2
advanced-health-menu-wounds = Повреждения
advanced-health-menu-equipment = Снаряжение
advanced-health-slot-hand = Рука
advanced-health-slot-belt = Пояс
advanced-health-slot-back = Спина
advanced-health-slot-pocket = Карман
advanced-health-slot-suit = Костюм
advanced-health-slot-id = Удостоверение

advanced-health-vital-immunity = Иммунозащита
advanced-health-zone-protection = Защищённость
advanced-health-zone-bleeding = Кровотечение

advanced-disease-unknown = неизвестная болезнь
advanced-disease-space-flu = космический грипп
advanced-disease-wound-fever = раневая лихорадка
advanced-disease-status-weakness = Слабость
advanced-disease-status-fever = Лихорадка
advanced-disease-too-weak-twohanded = Вы слишком слабы для двуручного оружия.

advanced-health-blood-group-tooltip = Жидкость тела: {$group}. Перенос кислорода: {$carry}%
advanced-health-transfusion-ok = Переливание совместимо. Объём восстанавливается.
advanced-health-transfusion-fluid = Инфузия раствора: объём восстановлен, но кислород не переносится.
advanced-health-transfusion-incompatible = НЕСОВМЕСТИМАЯ КРОВЬ! Гемолитическая реакция.
advanced-health-transfusion-incompatible-imperial = НЕСОВМЕСТИМАЯ КРОВЬ! Гемолитическая реакция.
advanced-health-transfusion-incompatible-molei = НЕСОВМЕСТИМАЯ ГЕМОЛИМФА! Свёртывание, слабость и сбой дыхания.
advanced-health-transfusion-incompatible-dwarf = НЕСОВМЕСТИМАЯ ГУСТАЯ КРОВЬ! Тромбоз, боль и замедление.
advanced-health-transfusion-incompatible-lavrite = НЕСОВМЕСТИМАЯ ТЕРМОКРОВЬ! Кристаллизация и шок.
advanced-health-transfusion-incompatible-kobold = НЕСОВМЕСТИМАЯ РЕПТИЛЬЯ КРОВЬ! Быстрый токсический шок.
advanced-health-transfusion-incompatible-saurian = НЕСОВМЕСТИМАЯ РЕПТИЛЬЯ КРОВЬ! Падение температуры и слабость.
advanced-health-transfusion-incompatible-therian = НЕСОВМЕСТИМАЯ КРОВЬ! Иммунная реакция и жар.
advanced-health-transfusion-incompatible-arkane = НЕСОВМЕСТИМЫЙ ИХОР! Нестабильная ожоговая реакция.
advanced-health-transfusion-incompatible-avian = НЕСОВМЕСТИМАЯ ЛЁГКАЯ КРОВЬ! Кислородное голодание.
advanced-health-transfusion-incompatible-elir = НЕСОВМЕСТИМАЯ ОЧИЩЕННАЯ КРОВЬ! Токсическая реакция на примеси.
advanced-health-transfusion-incompatible-slimefolk = НЕСОВМЕСТИМАЯ ПЛАЗМА! Плазменный шок и дестабилизация тела.

advanced-health-tissue-skin = Кожа
advanced-health-tissue-muscle = Мышцы
advanced-health-tissue-bone = Кость
advanced-health-tissue-vessel = Сосуды
advanced-health-tissue-nerve = Нервы
advanced-health-tissue-organ = Органы

advanced-health-wound-cut = Порез
advanced-health-wound-puncture = Прокол
advanced-health-wound-gunshot = Огнестрельное ранение
advanced-health-wound-bruise = Ушиб
advanced-health-wound-burn = Ожог
advanced-health-wound-fracture = Перелом
advanced-health-wound-shrapnel = Осколочное ранение
advanced-health-wound-organdamage = Повреждение органа
advanced-health-wound-nervedamage = Повреждение нерва
advanced-health-wound-row = {$type} · {$severity}
advanced-health-wound-flag-bleeding = кровотечение
advanced-health-wound-flag-bandaged = повязка
advanced-health-wound-flag-sutured = зашита
advanced-health-wound-flag-foreign = инородное тело

advanced-health-action-bandage = Перевязать
advanced-health-action-pressurebandage = Давящая повязка
advanced-health-action-tourniquet = Наложить жгут
advanced-health-action-splint = Наложить шину
advanced-health-action-hemostatic = Гемостатик
advanced-health-action-suture = Зашить рану
advanced-health-action-foreignbodyremoval = Извлечь инородное тело
advanced-health-action-requires = Требуется в руке: {$item}
advanced-health-action-bare-hand = Можно голой рукой (сложнее)
advanced-health-treatment-not-needed = Эта процедура здесь не требуется.

advanced-health-minigame-cancel = Отмена
advanced-health-minigame-success = Готово!
advanced-health-minigame-fail-generic = Не получилось — попробуйте снова.
advanced-health-minigame-fail-shake = Слишком сильно дёрнули — рана расширилась.
advanced-health-minigame-fail-suture = Промах — нить порвалась.

advanced-health-minigame-title-foreignbodyremoval = Извлечение осколка
advanced-health-minigame-title-bandage = Перевязка
advanced-health-minigame-title-pressurebandage = Давящая повязка
advanced-health-minigame-title-hemostatic = Гемостатик
advanced-health-minigame-title-suture = Наложение швов
advanced-health-minigame-title-splint = Шинирование
advanced-health-minigame-title-tourniquet = Жгут

advanced-health-minigame-hint-extraction-hand = Зажмите ЛКМ и тяните осколки вверх — их четыре. Можно отпускать и продолжать. Боль дрожит рукой.
advanced-health-minigame-hint-extraction-tool = Зажмите ЛКМ и тяните осколки вверх щипцами — их четыре. Не уводите в стороны.
advanced-health-minigame-hint-steady = Держите курсор в зелёной зоне, зажав ЛКМ.
advanced-health-minigame-hint-suture = Зашейте точки по порядку.
advanced-health-minigame-hint-splint = Совместите отломок кости и отпустите ЛКМ.
advanced-health-minigame-hint-tourniquet = Удерживайте ЛКМ в центре, затягивая жгут.

advanced-health-minigame-status-extraction = Осколок {$shard}/{$total} — {$percent}%
advanced-health-minigame-status-steady = Удержание: {$seconds} с
advanced-health-minigame-status-tourniquet = Затягивание: {$seconds} с
advanced-health-minigame-status-suture = Шов {$step}/{$total}
advanced-health-minigame-status-splint = Совместите кость и отпустите
advanced-health-cond-foreign-count = Инородное тело ×{$count}
advanced-health-minigame-hint-wrap = Зажмите ЛКМ и обводите рану по кругу. Каждый отрезок — 1% бинта и −0.01 л/м кровотечения. Весь рулон тратить необязательно.
advanced-health-minigame-status-wrap = Бинт: {$percent}% · Кровотечение: {$bleed} л/м
advanced-health-bandage-durability = Прочность бинта: {$percent}%
advanced-health-bandage-applied = Повязка наложена на { $part }. Осталось бинта: {$percent}%.
advanced-health-aim-menu-title = Прицел
advanced-health-aim-key-hint = Удерживайте { $key }, отпустите для выбора. Короткое нажатие — авто.
advanced-health-aim-penalty = Штраф к точности: {$value}
advanced-health-aim-too-fast = Слишком быстро!

advanced-health-unconscious-shock =
    Шок подавляет вас ({$shock}% при пороге {$threshold}%).
    Боль, кровопотеря и травмы вышибают из сознания — в ушах звенит, звуки приглушены.
advanced-health-unconscious-consciousness =
    Сознание падает ({$consciousness}%).
    Нехватка кислорода и шок снижают ясность — вы теряете контроль.
advanced-health-unconscious-both =
    Критическое состояние: сознание {$consciousness}%, шок {$shock}%.
    Вы без сознания — пульс бьётся в висках, мир уходит в туман.
advanced-health-consciousness-returned = Сознание возвращается. Дышите ровно.
