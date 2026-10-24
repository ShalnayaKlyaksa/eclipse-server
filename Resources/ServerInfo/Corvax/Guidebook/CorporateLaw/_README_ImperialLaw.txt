Архив содержит переписанную версию XML-документов под ИЗ — Имперский Закон Эйдосской Империи.

Что изменено:
- CorporateLaw.xml переименован по содержанию в Имперский Закон.
- CrimeList.xml полностью перестроен: 6 разделов, 24 главы, 144 статьи ИЗ-111...ИЗ-646.
- Существующие Chapter11.xml...Chapter43.xml переписаны.
- Добавлены новые главы: Chapter23.xml, Chapter24.xml, Chapter33.xml, Chapter34.xml, Chapter44.xml, Chapter51.xml...Chapter54.xml, Chapter61.xml...Chapter64.xml.
- Punishments.xml, Modificators.xml, OPRS.xml и Misc.xml переписаны без FTLTextpart, чтобы текст был прямо внутри XML.

Важно:
Если в твоём проекте нет GuideEntry-прототипов для новых глав, ссылки вида CLChapter51, CLChapter52 и т.п. в таблице будут вести в пустоту. Нужно добавить guideEntry для каждого нового ChapterXX.xml с id CLChapterXX либо переименовать ссылки под свои существующие id.

Старые id CLChapter11...CLChapter43 сохранены в ссылках, чтобы не ломать существующую структуру.
