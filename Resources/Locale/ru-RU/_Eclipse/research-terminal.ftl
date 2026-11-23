research-terminal-no-network = Нет подключения к серверу НИО
research-terminal-credentials-format = Проверьте раскладку клавиатуры и перепишите логин и пароль из своих реквизитов.
research-terminal-login-failed = Логин или пароль не подошли. Проверьте выданные реквизиты у главы НИО.
research-terminal-welcome = Вы вошли как { $user }. Введите help, чтобы открыть справочник.
research-terminal-use-login-form = Для реквизитов используйте форму входа. Пароли не вводятся в командную строку.
research-terminal-researched = Изучено: { $tech }.
research-terminal-unavailable = Недоступно: { $tech }. Проверьте предпосылки, направление и научные очки.
research-terminal-tech-not-found = Технология не найдена. Используйте techs <поиск>; подходят короткое имя или полное название.
research-terminal-batch-stopped = Пакет остановлен на { $tech }. Предыдущие успешные исследования сохранены.
research-terminal-unknown-command = Неизвестная команда. help — список команд.
research-terminal-status = { $user } / { $role } / lab-{ $address } / сеть: { $server } / { $points } ОИ
research-terminal-role-director = Администратор НИО
research-terminal-role-scientist = Исследователь
research-terminal-done = ИЗУЧЕНО
research-terminal-ready = ПРЕДПОСЫЛКИ ГОТОВЫ
research-terminal-locked = ЗАБЛОКИРОВАНО
research-terminal-plan-total = План: { $count } исследований, { $points } ОИ. plan ничего не покупает.
research-terminal-remote-usage = remote lab-N status — состояние; remote lab-N exit — закрыть свою сессию. Администратор может закрыть чужую.
research-terminal-remote-closed = Сессия lab-{ $address } завершена.
research-terminal-operation-denied = Операция запрещена для этой учётной записи.
research-terminal-remote-not-found = Терминал не найден в вашей сети. terminals — доступные адреса.
research-terminal-account-revoked = Учётная запись { $user } отозвана. Её открытые сессии закрыты.
research-terminal-analysis-cooldown = Анализ уже выполняется либо стенд восстанавливается после ошибки.
research-terminal-analysis-ineligible = Нужна ещё не изученная технология с готовыми предпосылками. Повторный анализ одной технологии в этой сети не даёт награды.
research-terminal-analysis-trace = АНАЛИЗ / 1: сигналы A={ $a }, B={ $b }, C={ $c }. Найдите ветку с наибольшим сигналом: trace <A/B/C>.
research-terminal-analysis-none = Нет активного анализа. Начните с analyze <технология>.
research-terminal-analysis-expired = Время анализа истекло. Запустите новый анализ.
research-terminal-analysis-failed = Проверка не пройдена. Данные сброшены; восстановление стенда — 45 секунд.
research-terminal-analysis-tune = АНАЛИЗ / 2: эталон { $reference }, измерено { $measured }. Введите знаковую поправку: tune <эталон минус измерение>.
research-terminal-analysis-verify = АНАЛИЗ / 3: байты { $a } { $b } { $c }. Сложите их по модулю 256 и подтвердите результат в HEX: verify <контрольная сумма>.
research-terminal-analysis-complete = Анализ принят. В сеть начислено { $points } ОИ. Технология по-прежнему изучается обычной командой research.
research-terminal-login-title = ВХОД В СЕТЬ НИО
research-terminal-username = ЛОГИН
research-terminal-password = ПАРОЛЬ
research-terminal-sign-in = Войти
research-terminal-login-help = За данными учётной записи обратитесь к главе научного отдела. В его отсутствие — к вышестоящему руководству.
research-terminal-graph-tab = Карта исследований
research-terminal-command-tab = Командная строка
research-terminal-session-hint = Esc: оставить сессию · exit: завершить
research-terminal-command-placeholder = help — команды · Tab — дополнение · Up/Down — история
research-terminal-send = Выполнить
research-terminal-login-required = Сначала откройте сессию учётной записи на консоли НИО.

research-discipline-cargo = Снабжение
research-discipline-medical = Медицина
research-discipline-robotics = Робототехника
research-discipline-security = Служба безопасности
research-discipline-shaft = Добыча
research-terminal-select-network = Выберите научный сервер. Отмеченный сервер уже подключён.
research-terminal-accounts-issued = Учётные записи выдаёт глава НИО. Обратитесь к нему за логином и паролем.
research-mail-program = Почта станции
research-mail-refresh = Проверить почту
research-mail-empty = Новых писем пока нет.
research-mail-credentials-subject = Ваш доступ к сети НИО
research-mail-credentials-body = { $name }, ваша учётная запись готова.
    Логин: { $login }
    Пароль: { $password }
    Эти данные подходят для исследовательских консолей станции. Когда закончите работу, введите exit.
research-mail-new-message = Вам пришло письмо от службы учётных записей.
research-mail-accounts-service = Служба учётных записей
research-mail-new-employee-subject = Прибыл новый сотрудник — данные для входа
research-credentials-report-header = РЕЕСТР УЧЁТНЫХ ЗАПИСЕЙ СТАНЦИИ
    Экземпляр главы НИО. Передавайте сотрудникам только их собственные реквизиты. Данные поздно прибывших приходят в почту вашего КПК.
research-map-group-factory = Производство
research-map-group-engineering = Инженерия
research-terminal-points-unit = ОИ
research-terminal-connected = Подключено к rd-{ $id }.
research-terminal-server-not-found = Такого сервера поблизости нет. Введите servers, чтобы увидеть список.
research-terminal-account-disabled = доступ отозван
research-terminal-role-usage = Управлять правами могут только руководители: role <логин> observer|researcher|admin.
research-terminal-role-updated = Права { $user } изменены: { $role }.
research-terminal-password-reset = Пароль { $user } заменён. Новые реквизиты отправлены в вашу почту; старые сессии закрыты.
research-mail-reset-subject = Новые реквизиты сотрудника
research-mail-reset-body = { $name }
    Логин: { $login }
    Новый пароль: { $password }
    Передайте эти данные сотруднику лично.
research-mail-player-subject = Сообщение из сети НИО
research-terminal-mail-sent = Письмо для { $user } отправлено.
research-terminal-mail-usage = mail inbox — список писем; mail read 1 — открыть письмо; mail send <логин> <текст> — написать сотруднику.
research-terminal-info-title = { $name }
    Команда: research { $alias }
    Стоимость: { $points } ОИ. Предварительные исследования:
research-terminal-queue-empty = Очередь пуста.
research-terminal-queue-cancelled = Оставшиеся задания сняты с очереди.
research-terminal-queue-usage = queue add <технология> — добавить одну; queue plan <цель> — добавить всю цепочку; queue list — проверить; queue cancel — остановить.
research-terminal-queue-added = В очереди { $count } исследований. Консоль продолжит работу по мере поступления очков. Esc оставит её работать; exit остановит очередь.
research-terminal-queue-idle = ОЧЕРЕДЬ: НЕТ ЗАДАНИЙ
research-terminal-queue-count = В ОЧЕРЕДИ: { $count }
research-terminal-reference-title = СПРАВОЧНИК
research-terminal-reference-overview = С чего начать
research-terminal-reference-science = Исследования и очередь
research-terminal-reference-analysis = Научный анализ
research-terminal-reference-network = Сеть и терминалы
research-terminal-reference-accounts = Сотрудники и почта
research-terminal-reference-hint = Выберите раздел — справа появятся команды с примерами.
    Tab дополняет команду или название исследования. Стрелки возвращают предыдущий ввод.
research-terminal-input-hint-overview = Начните с help. Например: find батареи или help science.
research-terminal-input-hint-science = find <название> -> info <имя> -> research <имя>. plan покажет всю цепочку и её цену.
research-terminal-input-hint-queue = queue plan <цель> добавит недостающие исследования. Консоль дождётся нужных очков сама.
research-terminal-input-hint-analysis = analyze <имя> запускает проверку: выберите сигнал, настройте прибор, сверьте контрольную сумму.
research-terminal-input-hint-network = servers — научные серверы; terminals — рабочие места; remote lab-N status — проверить выбранный терминал.
research-terminal-input-hint-accounts = users <имя> поможет найти логин. mail inbox покажет письма. Права, пароли и отзыв доступа меняют руководители.
research-terminal-help-overview = == С чего начать ==
    find <слово> — найти исследование.
    info <имя> — посмотреть цену и требования.
    research <имя> — изучить его.
    
    Пример: find схемы
    Затем: info electronics
    
    help science — цепочки и очередь.
    help analysis — заработать очки анализом.
    help network — серверы и другие терминалы.
    help accounts — сотрудники, права и почта.
    
    Tab дополняет ввод; Up/Down возвращают команды. Esc оставляет сессию, exit завершает её.
research-terminal-help-science = == Исследования ==
    find кремний — поиск по названию.
    info precisionelectronics — цена, условия и результаты.
    plan precisionelectronics — вся недостающая цепочка и её стоимость.
    research electronics — изучить одну технологию.
    batch electronics,automation — выполнить несколько по порядку.
    
    == Очередь ==
    queue plan precisionelectronics — поручить консоли всю цепочку.
    queue add metallurgy — добавить одно исследование.
    queue list — посмотреть задания.
    queue cancel — снять оставшиеся задания.
    
    Очередь ждёт поступления очков и работает после Esc. exit её останавливает.
research-terminal-help-analysis = == Анализ ==
    analyze electronics — подготовить проверку доступной технологии.
    1. Прибор покажет три сигнала. trace B выберет ветку B. Нужна самая сильная.
    2. Сравните измерение с эталоном. tune 13 добавит поправку +13.
    3. Сложите три байта по модулю 256. verify 71 отправит сумму 71 в шестнадцатеричной записи.
    
    Успешная проверка даёт от 100 до 600 ОИ. Награда общая для сети и выдаётся один раз на технологию. Ошибка прервёт анализ; стенду понадобится 45 секунд на восстановление.
research-terminal-help-network = == Сеть НИО ==
    status — ваша сессия, сервер и научные очки.
    servers — доступные научные серверы.
    connect 2 — подключить консоль к серверу rd-2.
    terminals — адреса рабочих мест и открытые сессии.
    remote lab-3 status — проверить терминал lab-3.
    remote lab-3 exit — завершить свою сессию на нём. Руководитель может завершить чужую.
    history — последние команды этого терминала.
    
    clear очистит экран. exit завершит вашу сессию и остановит очередь.
research-terminal-help-accounts = == Сотрудники и почта ==
    users Смирнова — найти сотрудника и его логин.
    mail inbox — ваши письма.
    mail read 1 — открыть первое письмо.
    mail send crew007 Нужен образец сплава — написать коллеге.
    
    == Для руководителей ==
    role crew007 researcher — дать право исследований.
    role crew007 observer — оставить только просмотр.
    role crew007 admin — передать управление учётными записями.
    reset crew007 — заменить пароль; реквизиты придут в вашу почту.
    revoke crew007 — отозвать доступ и закрыть сессии.
    
    Список исходных реквизитов выдаётся главе НИО. Данные поздно прибывших приходят ему в КПК.

research-map-short-factory = Фабрика
research-map-short-industrial = Инж.
research-map-short-experimental = НИО
research-map-short-robotics = Роботы
research-map-short-security = СБ
research-map-short-medical = Мед.
research-map-short-civilianservices = Сервис
research-map-short-cargo = Снабж.
research-map-short-shaft = Добыча
research-map-short-arsenal = Арсенал
research-mail-copy = Копировать письмо
ent-ResearchCredentialsReport = реестр учётных записей станции
    .desc = Поимённый список сотрудников с реквизитами доступа. Экземпляр главы научного отдела.
ent-ResearchMailCartridge = картридж почты станции
    .desc = Для служебной переписки и распоряжений руководства.

research-terminal-network-title = Научные серверы
research-terminal-role-observer = Наблюдатель

research-node-industrialelectronics = Первые платы
research-node-industrialautomation = Станки I
research-node-industrialprocesschemistry = Химия
research-node-industrialmetallurgy = Сплавы
research-node-industrialprecisionelectronics = Точные платы
research-node-industrialadvancedautomation = Станки II
research-node-industrialsalvageequipment = Добыча
research-node-industrialsecurityequipment = Оснащение СБ
research-node-industrialscienceequipment = Приборы НИО
research-node-industrialperfectautomation = Станки III
research-node-industrialperfectmaterials = Соверш. сплавы
research-node-industrialperfectequipment = Соверш. оснащение
research-node-industrialenergystorage = Батареи
