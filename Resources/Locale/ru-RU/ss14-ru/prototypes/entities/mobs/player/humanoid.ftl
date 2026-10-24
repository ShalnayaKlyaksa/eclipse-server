ent-RandomHumanoidSpawnerDeathSquad = Агент Эскадрона смерти
    .desc = { "" }
    .suffix = Роль ИКР, Эскадрон смерти

# ERT Leader
ent-RandomHumanoidSpawnerERTLeader = ИКР лидер
    .suffix = Роль ИКР, Базовый
    .desc = { "" }
ent-RandomHumanoidSpawnerERTLeaderEVA = ИКР лидер
    .suffix = Роль ИКР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
ent-RandomHumanoidSpawnerERTLeaderArmed = { ent-RandomHumanoidSpawnerERTLeaderEVA }
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен XL8, 4 запасных магазина разного типа.

# ERT Chaplain
ent-RandomHumanoidSpawnerERTChaplain = ИКР священник
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ИКР, Базовый
ent-RandomHumanoidSpawnerERTChaplainEVA = ИКР священник
    .suffix = Роль ИКР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTChaplain.desc }

# ERT Janitor
ent-RandomHumanoidSpawnerERTJanitor = ИКР уборщик
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ИКР, Базовый
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
ent-RandomHumanoidSpawnerERTJanitorEVA = ИКР уборщик
    .suffix = Роль ИКР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTJanitor.desc }

# ERT Engineer
ent-RandomHumanoidSpawnerERTEngineer = ИКР инженер
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ИКР, Базовый
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
ent-RandomHumanoidSpawnerERTEngineerEVA = { ent-RandomHumanoidSpawnerERTEngineer }
    .suffix = Роль ИКР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTEngineer.desc }
ent-RandomHumanoidSpawnerERTEngineerArmed = { ent-RandomHumanoidSpawnerERTEngineer }
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен Силовиком, имеет детонационный шнур и коробку детонаторов.

# ERT Security
ent-RandomHumanoidSpawnerERTSecurity = ИКР офицер безопасности
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ИКР, Базовый
ent-RandomHumanoidSpawnerERTSecurityEVA = ИКР офицер безопасности
    .suffix = Роль ИКР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTSecurity.desc }
ent-RandomHumanoidSpawnerERTSecurityArmedRifle = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Стрелок
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен Лектером, 4 запасных магазина различного типа, Лазерная пушка и переносной зарядник.
ent-RandomHumanoidSpawnerERTSecurityArmedGrenade = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Гренадер
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен Гидрой с осколочными снарядами, имеет в запасе 6 фугасных, 3 ЭМИ и светошумовых снаряда.
ent-RandomHumanoidSpawnerERTSecurityArmedVanguard = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Авангард
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен WT550, 4 запасных магазина, 3 телескопических щита.
ent-RandomHumanoidSpawnerERTSecurityArmedShotgun = { ent-RandomHumanoidSpawnerERTSecurityEVA }, Сапёр
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен Силовиком, 3 коробки различной дроби, осколочной гранатой, детонационным шнуром и коробкой детонаторов.

# ERT Medic
ent-RandomHumanoidSpawnerERTMedical = ИКР медик
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
    .suffix = Роль ИКР, Базовый
    .desc = { ent-RandomHumanoidSpawnerERTLeader.desc }
ent-RandomHumanoidSpawnerERTMedicalEVA = ИКР медик
    .suffix = Роль ИКР, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTMedical.desc }
ent-RandomHumanoidSpawnerERTMedicalArmed = ИКР медик
    .suffix = Роль ИКР, Вооружен, ВКД
    .desc = Вооружен Лектером, 4 запасных магазина разного типа.

# CBURN
ent-RandomHumanoidSpawnerCBURNUnit = Агент РХБЗЗ
    .desc = { "" }
    .suffix = Роль ИКР
    .desc = { "" }

# misc
ent-RandomHumanoidSpawnerCentcomOfficial = Представитель Эйдосской империи
    .desc = { "" }
ent-RandomHumanoidSpawnerLegionAgent = Агент Легиона
    .desc = { "" }
ent-RandomHumanoidSpawnerNukeOp = Оперативник Легиона пепла (Не использовать)
    .desc = { "" }
ent-RandomHumanoidSpawnerCluwne = Клувень
    .desc = { "" }
    .suffix = Спавнит клувеня
ent-RandomHumanoidSpawnerERTLeaderEVALecter = { ent-RandomHumanoidSpawnerERTLeaderEVA }
    .suffix = Роль ИКР, Лектер, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTLeaderEVA.desc }
ent-RandomHumanoidSpawnerERTSecurityEVALecter = { ent-RandomHumanoidSpawnerERTSecurityEVA }
    .suffix = Роль ИКР, Лектер, ВКД
    .desc = { ent-RandomHumanoidSpawnerERTSecurityEVA.desc }
