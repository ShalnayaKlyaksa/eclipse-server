commerce-title = Платёжный терминал
commerce-refresh = Обновить счёт
commerce-claim = Зарегистрировать на мой ID
commerce-management = Управление кассой
commerce-help = Загрузите товар из руки, затем выставьте счёт. После оплаты товар получит покупатель. Без товара счёт оплачивает услугу. Комиссия включена в цену. Управление доступно по ID-карте владельца.
commerce-description = Назначение платежа
commerce-amount = Общая цена, кр.
commerce-recipient = Куда идёт выручка
commerce-personal-id = ID получателя
commerce-personal = Личный счёт
commerce-department = Бюджет отдела
commerce-commission = Комиссия, % (0–100)
commerce-fee-department = Получатель комиссии
commerce-issue = Выставить новый счёт
commerce-load = Загрузить товар из руки
commerce-return = Забрать товар
commerce-invoice = Владелец: { $owner }
    Назначение: { $description }
    Товар / услуга: { $item }
    Получатель: { $receiver }
    Итого: { $price } кредитов
    Получателю: { $net } кр. • Комиссия: { $fee } кр. ({ $percent }%) → { $department }
commerce-service = Услуга без автоматической выдачи
commerce-pay = Подтвердить оплату — { $price } кр.
commerce-no-invoice = Нет неоплаченного счёта
commerce-success = Операция выполнена.
commerce-failed = Операция отклонена. Проверьте ID-карту, права, остаток и актуальность счёта.
commerce-no-funds = Нужна ID-карта со счётом этой станции и достаточно кредитов.
commerce-panel-open = Сервисная панель кассы открыта. Мультитул позволяет перепривязать владельца.
commerce-panel-closed = Сервисная панель кассы закрыта.
commerce-hacked = Защита кассы обойдена. Управление привязано к вашей ID-карте.
commerce-price = { $price } кр.
commerce-print-price = Печать: { $price } кр. за документ. Резерв при заказе, возврат за отменённое.
commerce-print-failed = Заказ отклонён: проверьте бумагу, количество, цену, ID-карту и остаток кредитов.
commerce-reserve = Резерв оплаты товара / печати
commerce-refund = Возврат резерва
commerce-print-entry = Печать документа → бюджет сервиса
commerce-vend-entry = Покупка в вендинге → бюджет сервиса
commerce-purchase-entry = Оплата: { $description }
ent-ImperialPaymentTerminal = платёжный терминал
    .desc = Переносная касса для продажи товаров и услуг. Получатель — личный счёт или бюджет отдела. Управление по ID-карте владельца. Панель вскрывается отвёрткой, защита обходится мультитулом или емагом.
ent-ImperialBarPaymentTerminal = касса бара
    .desc = Касса бармена. Цены определяет тот, кто контролирует ID владельца или сумел взломать устройство.
ent-ImperialKitchenPaymentTerminal = касса кухни
    .desc = Касса повара для продажи блюд и услуг.
