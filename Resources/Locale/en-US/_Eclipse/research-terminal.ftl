research-terminal-no-network = No research network connection
research-terminal-credentials-format = Username: 3–24 ASCII letters, digits, _, - or dot. In-game password: 4–32 printable ASCII characters without spaces.
research-terminal-login-failed = Incorrect username or password for this research network.
research-terminal-welcome = Session opened for { $user }. help: commands; Esc: leave session open; exit: terminate.
research-terminal-use-login-form = Use the sign-in form for credentials. Passwords do not belong in command history.
research-terminal-researched = Researched: { $tech }.
research-terminal-unavailable = Unavailable: { $tech }. Check prerequisites, discipline and research points.
research-terminal-tech-not-found = Technology not found. Use techs <filter>; aliases and full names are accepted.
research-terminal-batch-stopped = Batch stopped at { $tech }. Earlier successful research is preserved.
research-terminal-unknown-command = Unknown command. Enter help for commands.
research-terminal-status = { $user } / { $role } / lab-{ $address } / network: { $server } / { $points } RP
research-terminal-role-director = Research administrator
research-terminal-role-scientist = Researcher
research-terminal-done = RESEARCHED
research-terminal-ready = PREREQUISITES MET
research-terminal-locked = LOCKED
research-terminal-plan-total = Plan: { $count } technologies, { $points } RP. Planning does not purchase anything.
research-terminal-remote-usage = remote lab-N status: inspect; remote lab-N exit: close your session. Administrators can close other sessions.
research-terminal-remote-closed = Session on lab-{ $address } terminated.
research-terminal-operation-denied = This account cannot perform that operation.
research-terminal-remote-not-found = Terminal not found on your network. Enter terminals for available addresses.
research-terminal-account-revoked = Account { $user } revoked. Its open sessions have been terminated.
research-terminal-analysis-cooldown = An analysis is in progress or the bench is recovering from an error.
research-terminal-analysis-ineligible = Select an unresearched technology with its prerequisites met. Each technology rewards analysis once per network.
research-terminal-analysis-trace = ANALYSIS / 1: signals A={ $a }, B={ $b }, C={ $c }. Trace the strongest signal: trace <A/B/C>.
research-terminal-analysis-none = No analysis in progress. Start with analyze <technology>.
research-terminal-analysis-expired = Analysis timed out. Start a new analysis.
research-terminal-analysis-failed = Verification failed. Data discarded; bench recovery takes 45 seconds.
research-terminal-analysis-tune = ANALYSIS / 2: reference { $reference }, measured { $measured }. Enter the signed correction: tune <reference minus measurement>.
research-terminal-analysis-verify = ANALYSIS / 3: bytes { $a } { $b } { $c }. Add them modulo 256 and confirm the HEX result: verify <checksum>.
research-terminal-analysis-complete = Analysis accepted. Network awarded { $points } RP. Purchase the technology with research as usual.
research-terminal-login-title = RESEARCH NETWORK SIGN-IN
research-terminal-username = USERNAME
research-terminal-password = PASSWORD
research-terminal-sign-in = Sign in
research-terminal-login-help = Contact the Research Director for your account details. In their absence, contact a higher-ranking officer.
research-terminal-graph-tab = Research map
research-terminal-command-tab = Command line
research-terminal-session-hint = Esc: leave session open · exit: terminate
research-terminal-command-placeholder = help: commands · Tab: complete · Up/Down: history
research-terminal-send = Run
research-terminal-login-required = Sign in to the research terminal first.

research-discipline-cargo = Cargo
research-discipline-medical = Medicine
research-discipline-robotics = Robotics
research-discipline-security = Security
research-discipline-shaft = Mining
research-terminal-select-network = Choose a research server. The marked server is connected.
research-terminal-accounts-issued = The research director issues accounts. Ask them for your login and password.
research-mail-program = Station mail
research-mail-refresh = Check mail
research-mail-empty = No messages yet.
research-mail-credentials-subject = Your research network account
research-mail-credentials-body = { $name }, your account is ready.
    Username: { $login }
    Password: { $password }
    Use these credentials at station research terminals. Enter exit when you finish.
research-mail-new-message = You have mail from the accounts service.
research-mail-accounts-service = Accounts service
research-mail-new-employee-subject = New arrival — account credentials
research-credentials-report-header = STATION ACCOUNT REGISTER
    Research director copy. Give employees only their own credentials. Late arrival credentials are sent to your PDA mailbox.
research-map-group-factory = Manufacturing
research-map-group-engineering = Engineering
research-terminal-points-unit = RP
research-terminal-connected = Connected to rd-{ $id }.
research-terminal-server-not-found = No such server nearby. Enter servers to see the list.
research-terminal-account-disabled = access revoked
research-terminal-role-usage = Only administrators can change roles: role <username> observer|researcher|admin.
research-terminal-role-updated = Updated { $user } permissions: { $role }.
research-terminal-password-reset = Password reset for { $user }. New credentials are in your mailbox; old sessions are closed.
research-mail-reset-subject = Employee password reset
research-mail-reset-body = { $name }
    Username: { $login }
    New password: { $password }
    Give these credentials to the employee in person.
research-mail-player-subject = Research network message
research-terminal-mail-sent = Message sent to { $user }.
research-terminal-mail-usage = mail inbox: list messages; mail read 1: open a message; mail send <username> <text>: write to an employee.
research-terminal-info-title = { $name }
    Command: research { $alias }
    Cost: { $points } RP. Prerequisites:
research-terminal-queue-empty = The queue is empty.
research-terminal-queue-cancelled = Remaining jobs removed from the queue.
research-terminal-queue-usage = queue add <technology>: add one; queue plan <goal>: add its prerequisite chain; queue list: inspect; queue cancel: stop.
research-terminal-queue-added = Queued { $count } technologies. Work continues as points arrive. Esc leaves it running; exit stops the queue.
research-terminal-queue-idle = QUEUE: NO JOBS
research-terminal-queue-count = QUEUED: { $count }
research-terminal-reference-title = QUICK REFERENCE
research-terminal-reference-overview = Getting started
research-terminal-reference-science = Research and queue
research-terminal-reference-analysis = Analysis
research-terminal-reference-network = Network and terminals
research-terminal-reference-accounts = Employees and mail
research-terminal-reference-hint = Choose a section to see commands and examples.
    Tab completes commands and research aliases. Arrow keys recall previous input.
research-terminal-input-hint-overview = Start with help. Try find battery or help science.
research-terminal-input-hint-science = find <name> -> info <alias> -> research <alias>. plan shows prerequisites and total cost.
research-terminal-input-hint-queue = queue plan <goal> adds missing research. The terminal waits for points automatically.
research-terminal-input-hint-analysis = analyze <alias> starts a check: trace the signal, tune the instrument and verify the checksum.
research-terminal-input-hint-network = servers lists research servers; terminals lists workstations; remote lab-N status inspects one.
research-terminal-input-hint-accounts = users <name> finds usernames. mail inbox lists messages. Only administrators change roles, reset passwords or revoke access.
research-terminal-help-overview = == Getting started ==
    find <word>: search technologies.
    info <alias>: show cost and prerequisites.
    research <alias>: purchase research.
    
    Example: find circuit
    Then: info electronics
    
    help science: chains and queue.
    help analysis: earn points through analysis.
    help network: servers and other terminals.
    help accounts: employees, permissions and mail.
    
    Tab completes input; Up/Down recall commands. Esc leaves the session open; exit terminates it.
research-terminal-help-science = == Research ==
    find silicon: search by name.
    info precisionelectronics: cost, requirements and results.
    plan precisionelectronics: missing chain and total cost.
    research electronics: purchase one technology.
    batch electronics,automation: purchase several in order.
    
    == Queue ==
    queue plan precisionelectronics: schedule the whole chain.
    queue add metallurgy: add one technology.
    queue list: show jobs.
    queue cancel: remove remaining jobs.
    
    The queue waits for points and keeps running after Esc. exit stops it.
research-terminal-help-analysis = == Analysis ==
    analyze electronics: start a check on an available technology.
    1. Compare three signals. trace B chooses branch B. Choose the strongest.
    2. Compare the measurement to its reference. tune 13 adds a +13 correction.
    3. Add three bytes modulo 256. verify 71 submits hexadecimal checksum 71.
    
    A successful check earns 100–600 RP, once per technology across the whole network. A mistake ends the check and requires 45 seconds of recovery.
research-terminal-help-network = == Research network ==
    status: your session, server and points.
    servers: available research servers.
    connect 2: connect to rd-2.
    terminals: workstations and open sessions.
    remote lab-3 status: inspect lab-3.
    remote lab-3 exit: close your session there; administrators can close others.
    history: recent terminal commands.
    
    clear clears the screen. exit ends the session and stops its queue.
research-terminal-help-accounts = == Employees and mail ==
    users Smith: find an employee and their username.
    mail inbox: your messages.
    mail read 1: open the first message.
    mail send crew007 Alloy sample needed: write to a colleague.
    
    == Administrators ==
    role crew007 researcher: grant research access.
    role crew007 observer: allow read access only.
    role crew007 admin: grant account management.
    reset crew007: reset password; credentials arrive in your inbox.
    revoke crew007: revoke access and close sessions.
    
    The research director receives the initial credential register and PDA messages for late arrivals.

research-map-short-factory = Factory
research-map-short-industrial = Eng.
research-map-short-experimental = R&D
research-map-short-robotics = Robotics
research-map-short-security = Security
research-map-short-medical = Med.
research-map-short-civilianservices = Service
research-map-short-cargo = Cargo
research-map-short-shaft = Mining
research-map-short-arsenal = Arsenal
research-mail-copy = Copy message
ent-ResearchCredentialsReport = station account register
    .desc = Employee names and account details. The Research Director’s copy.
ent-ResearchMailCartridge = station mail cartridge
    .desc = For station correspondence and messages from command.

research-terminal-network-title = Scientific servers
research-terminal-role-observer = Observer

research-node-industrialelectronics = First boards
research-node-industrialautomation = Machines I
research-node-industrialprocesschemistry = Chemistry
research-node-industrialmetallurgy = Alloys
research-node-industrialprecisionelectronics = Precision boards
research-node-industrialadvancedautomation = Machines II
research-node-industrialsalvageequipment = Salvage
research-node-industrialsecurityequipment = Security gear
research-node-industrialscienceequipment = Science gear
research-node-industrialperfectautomation = Machines III
research-node-industrialperfectmaterials = Perfect alloys
research-node-industrialperfectequipment = Perfect gear
research-node-industrialenergystorage = Batteries
