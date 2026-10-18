# Frost Melee (ледяные оружия серии HardcoreMelee)

Мод перекраивает ванильное ледяное оружие под доктрину серии (philosophy.md в HardcoreMelee): оружие сильнее вблизи, набор приёмов один — взмах, тычки, выставленное оружие (встреча/копьё), бросок без автовозврата. ЛКМ — взмах, ПКМ тап — выставляемый режим (тык-тумблер), ПКМ зажать — тычки, ПКМ + ЛКМ — бросок.

## Подсистемы

- Замороженное копьё (ванильная Ледяная коса, ItemID.IceSickle): полная система приёмов + шкала заморозки. sickle.md.
- Ледяные мечи (Ice Blade, Frostbrand): тот же набор приёмов без шкалы, взмах-дуга и «встреча». swords.md.
- Йо-йо Амарок: свой полёт вместо ванильного, наводка, круг, нарастающий лёд. amarok.md.
- Северный полюс и Ледяной бумеранг — пока ванилла 1:1 (заглушки GlobalItem с TODO).

## Структура

- FrostMelee.cs — подмена текстур в Load (серп → IceSpearHeld, мечи → Ice_Blade/Frostbrand), позы для Weapon Out Lite, восстановление при выгрузке.
- Content/Sickle* — мозг приёмов (SicklePlayer), глобалка предмета (SickleGlobalItem), геометрия спрайта (SickleSprite), слои рисования (SickleThrowPoseLayer, SickleSpinSwingLayer), текстуры (SickleTextures), индикатор над головой (SickleUI), ФРОЗЕН-вид (FrozenSpear).
- Content/Projectiles — дуга взмаха (SickleArcProjectile), тычок (SickleStabProjectile), выставленное копьё (SickleBraceProjectile), бросок (SickleThrowProjectile), зона инея от взрыва (FrostZoneSystem).
- Content/Items — глобалка серпа, ThrownLockItem (блокировка предмета в полёте).
- Content/Weapons — мечи: SwordPlayer, SwordGlobalItem, SwordTypes, SwordArcProjectile, SwordStabProjectile, SwordThrowPoseLayer, SwordUI + заглушки NorthPole / IceBoomerang и каркас SpearPlayer.
- Content/Yoyo — AmarokPlayer (ввод), AmarokGlobalItem, AmarokYoyoProjectile, AmarokShardProjectile, AmarokCounterweightGlobal, YoyoUI.
- StabRibbon.cs — вершинная лента-шлейф тычка.
- FrostMeleeConfig.cs — клиентские настройки: индикатор приёма и подсказки управления.
- Localized RU/EN через hjson.

## Состояние

- Готовы: копьё (система серпа), мечи, Амарок, зона инея, индикатор приёма, конфиг, локализация.
- Не начато: Северный полюс, Ледяной бумеранг (только TODO), тумблер встрече у Севера.
- Мусор в репозитории: *.bak-версии снарядов (Content/Yoyo, Content/Projectiles) и старые отладочные заметки .cursor о несуществующих файлах (IceSpearProjectile, DeathSpearProjectile) — убрать при чистке.

## Зависимости

- Мод Weapon Out Lite — мягкая (Mod.Call через try/catch, отсутствие не мешает).
- Наши снаряды мечей переиспользуют снаряды серпа (рисуют любой предмет по ai[2], меряя спрайт по пикселям) — новые проекты для мечей не дублируются.
