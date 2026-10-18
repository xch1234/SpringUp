namespace SpringUp.UI
{
    // Translate at the presentation boundary; domain error codes remain stable for callers/tests.
    public static class ChineseText
    {
        public static string Error(string code) => code switch
        {
            "rest_only" or "lab actions only in lab" => "仅可在休整期间操作。",
            "laboratory_required" => "请靠近 LAB 柜台按 E 进入实验室后调整器官。",
            "capacity" => "背包容量不足，请先出售或丢弃物品。",
            "insufficient_marrow" => "骨髓不足，无法完成操作。",
            "sold_out" => "该商品已购买，请刷新商品或等待下一波。",
            "stale_offer" => "商品已刷新，请重新选择。",
            "no_shop_items" => "暂无可刷新的商品。",
            "refresh_limit" => "已达到刷新次数上限。",
            "unknown_item" => "商品不存在。",
            "not_open" => "该功能暂未开放。",
            "equip only from unequipped" => "请先将物品从装备槽拆下。",
            "part mismatch" => "物品与所选部位不匹配。",
            "empty slot" => "所选槽位没有装备。",
            "stale source" => "物品位置已变化，请重新拖动。",
            "invalid part" or "invalid slot" => "请选择有效的部位和槽位。",
            "slot occupied" => "所选槽位已有装备。",
            "cannot start next wave" => "暂时无法进入下一波，请检查阶段和背包容量。",
            _ => "操作未完成，请重新选择物品后重试。"
        };
    }
}
