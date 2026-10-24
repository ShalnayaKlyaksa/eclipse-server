interaction-LookAt-name = Смотреть
interaction-LookAt-description = Посмотреть на цель.
interaction-LookAt-success-self-popup = Вы смотрите на {THE($target)}.
interaction-LookAt-success-target-popup = Вы чувствуете, как {THE($user)} смотрит на вас...
interaction-LookAt-success-others-popup = {THE($user)} смотрит на {THE($target)}.

interaction-Hug-name = Обнять
interaction-Hug-description = Обнять цель.
interaction-Hug-success-self-popup = Вы обнимаете {THE($target)}.
interaction-Hug-success-target-popup = {THE($user)} обнимает вас.
interaction-Hug-success-others-popup = {THE($user)} обнимает {THE($target)}.

interaction-KnockOn-name = Постучать
interaction-KnockOn-description = Постучать по цели, чтобы привлечь внимание.
interaction-KnockOn-success-self-popup = Вы стучите по {THE($target)}.
interaction-KnockOn-success-target-popup = {THE($user)} стучит по вам.
interaction-KnockOn-success-others-popup = {THE($user)} стучит по {THE($target)}.

# The below includes conditionals for if the user is holding an item
interaction-WaveAt-name = Помахать
interaction-WaveAt-description = Помахать цели. Если вы держите предмет, вы помашете им.
interaction-WaveAt-success-self-popup = Вы машете {$hasUsed ->
    [false] {THE($target)}.
    *[true] своим {$used} в сторону {THE($target)}.
}
interaction-WaveAt-success-target-popup = {THE($user)} машет {$hasUsed ->
    [false] вам.
    *[true] своим {$used} вам.
}
interaction-WaveAt-success-others-popup = {THE($user)} машет {$hasUsed ->
    [false] {THE($target)}.
    *[true] своим {$used} в сторону {THE($target)}.
}
