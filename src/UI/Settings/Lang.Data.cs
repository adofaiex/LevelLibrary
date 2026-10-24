namespace LevelLibrary;

/// <summary>多语言词条表（以中文原文为 key）。
/// 分成多个 AddBatches 方法，方便继续补词条；没登记的条目会原样显示中文。</summary>
public static class LangData {
    static LangData() {
        Batch1();
        Batch2();
        // 这里是显式调用：LangData2~4 各自的静态构造器不会有人碰（那个坑见 Lang.cs），
        // 每个类都得自己调一次
        LangData2.Batch3();          // 成就的名字与触发条件
        LangData3.Batch4();          // 里程碑（含 {0} 模板）+ 余下的成就注释
        LangData3.Batch5();          // 大肥鱼 51 句台词 + 3 句到点插播
        LangData4.Batch6();          // 界面文案（含拼接串的 {0} 模板）
        LangData4.Batch7();          // 拼接串改成 TrFmt 之后新增的 {0} 模板
        LangData4.Batch8();          // 经数组/参数传进去的文本（右键菜单 / 各级 tab 名 / 按钮 …）
        LangData4.Batch9();          // 顶部导航排 + 主界面大厅（标签 / 统计条 / 公告横幅）
        LangData4.Batch10();         // TUF 社区定级（批量评级窗口）
        LangData5.Batch11();         // 去特效（标签 / 分类 / 分组 / 预设）+ 主界面与设置页补漏
        LangData5.Batch12();         // 全量补漏：此前没过 Tr 的界面文案包上 Tr 之后，把这批词条一次补齐
        LangData5.Batch13();         // 官谱详情（存档数据/通关记录/寄录图）+ 成就弹窗/判定槽位词
        LangOfficial.Register();     // 官方关卡名（从游戏本地化表生成，别手写）
    }

    /// <summary>空方法，唯一作用是被人调用一次以触发本类的静态构造器，把词条注册进表。
    /// 别删：本类是纯注册表、没有任何自然调用点，删了就回到"两张表永远是空的、换语言没反应"那个 bug。
    /// 调用点：`Lang` 的静态构造器（全项目唯一一处，任何走 `Tr` / `Current` 的路径都会经过）。</summary>
    public static void EnsureRegistered() { }

    /// <summary>第一批：主界面骨架 / 设置页 / 面板与详情区 / 各类对话框按钮</summary>
    private static void Batch1() {
        // ---------- 顶层 tab 与主界面 ----------
        Lang.Add("库", "Library", "라이브러리");
        Lang.Add("设置", "Settings", "설정");
        Lang.Add("开发者", "Developers", "개발자");
        Lang.Add("打开谱面库", "Open Chart Library", "채보 라이브러리 열기");
        Lang.Add("更换快捷键", "Change Hotkey", "단축키 변경");
        Lang.Add("扫描文件夹批量导入", "Scan Folder & Batch Import", "폴더 검사 후 일괄 가져오기");
        Lang.Add("获取更新：点击前往", "Get Updates: Click here", "업데이트 받기: 여기를 클릭");
        Lang.Add("成就投稿/BUG反馈", "Achievement Submissions / Bug Reports", "업적 제보 / 버그 신고");
        Lang.Add("正在播放", "Now Playing", "재생 중");
        Lang.Add("正在播放：", "Now Playing: ", "재생 중: ");
        Lang.Add("本周称号", "Weekly Title", "주간 칭호");
        Lang.Add("本周数据", "Weekly Stats", "주간 통계");
        Lang.Add("本周通关最难谱", "Hardest Clear This Week", "이번 주 최고 난도 클리어");
        Lang.Add("本周完全摆烂", "Total Slacking This Week", "이번 주는 완전히 놀았음");
        Lang.Add("本周玩得最多的谱", "Most Played This Week", "이번 주 최다 플레이");
        Lang.Add("本周玩家", "Player of the Week", "이번 주 플레이어");
        Lang.Add("周报", "Weekly Report", "주간 리포트");

        // ---------- 设置页：主/子 tab ----------
        Lang.Add("基础设置", "General", "일반 설정");
        Lang.Add("个性化", "Personalize", "개인 설정");
        Lang.Add("快捷键", "Hotkeys", "단축키");
        Lang.Add("检验", "Verify", "검사");
        Lang.Add("基本", "Basic", "기본");
        Lang.Add("存储", "Storage", "저장");
        Lang.Add("个人信息", "Player Info", "플레이어 정보");
        Lang.Add("背景", "Background", "배경");

        // ---------- 设置项 ----------
        Lang.Add("录入需确认", "Confirm Before Recording", "기록 전 확인");
        Lang.Add("批量导入谱面", "Batch Import Charts", "채보 일괄 가져오기");
        Lang.Add("谱面导入后备份", "Backup After Import", "가져온 후 백업");
        Lang.Add("自动标记", "Auto Mark", "자동 표시");
        Lang.Add("自定义备份位置", "Custom Backup Folder", "백업 폴더 지정");
        Lang.Add("打开备份位置", "Open Backup Folder", "백업 폴더 열기");
        Lang.Add("选择图片", "Choose Image", "이미지 선택");
        Lang.Add("修改", "Edit", "수정");
        Lang.Add("界面外显示", "Show Outside Panel", "패널 밖에도 표시");
        // 个人信息栏与大肥鱼两个设置页共用这一句（改文案两处一起变）
        Lang.Add("游玩时隐藏", "Hide While Playing", "플레이 중 숨기기");
        // 「随机发言」那一行的三个档位（点一下换一档）
        Lang.Add("随机发言", "Random Chatter", "랜덤 발언");
        Lang.Add("无", "Off", "없음");
        Lang.Add("每秒 1%", "1% / sec", "초당 1%");
        Lang.Add("每秒 0.1%", "0.1% / sec", "초당 0.1%");
        Lang.Add("显示个人信息栏", "Show Player Info", "플레이어 정보 표시");
        Lang.Add("X 位置", "X Position", "X 위치");
        Lang.Add("Y 位置", "Y Position", "Y 위치");
        Lang.Add("自定义背景", "Custom Background", "커스텀 배경");
        Lang.Add("背景音乐音量", "BGM Volume", "배경음 볼륨");
        Lang.Add("谱面列表字体大小", "Chart List Font Size", "채보 목록 글자 크기");
        // 成就两个字号设置的行标签
        Lang.Add("成就名称字体大小", "Achievement Name Font Size", "업적 이름 글자 크기");
        Lang.Add("成就详细信息字体大小", "Achievement Detail Font Size", "업적 상세 글자 크기");
        // 「面板背景模糊」：滑条改的是遮罩后面有多糊
        Lang.Add("面板背景模糊", "Panel Background Blur", "패널 배경 흐림");
        Lang.Add("显示语言", "Language", "표시 언어");
        Lang.Add("打开选中谱面", "Open Selected Chart", "선택한 채보 열기");
        Lang.Add("随机谱面并打开", "Open Random Chart", "랜덤 채보 열기");
        Lang.Add("向上切换 / 向下切换", "Previous / Next", "위로 / 아래로 전환");
        Lang.Add("记录详细日志", "Verbose Logging", "상세 로그 기록");
        Lang.Add("测试报错弹窗", "Test Error Popup", "오류 팝업 테스트");
        // 「检查界面字体」：查"全世界变楷体 / 字不见了"那两个毛病
        Lang.Add("检查界面字体", "Check UI Font", "UI 글꼴 검사");
        Lang.Add("开始检查", "Run Check", "검사 시작");
        Lang.Add("重复谱面检验", "Duplicate Check", "중복 채보 검사");
        Lang.Add("导出手动标记数据", "Export Manual Marks", "수동 표시 데이터 내보내기");
        Lang.Add("点我测试", "Click to test", "클릭해서 테스트");
        Lang.Add("选择文件夹导入", "Choose Folder", "폴더 선택");
        Lang.Add("选择备份文件夹", "Choose Backup Folder", "백업 폴더 선택");
        Lang.Add("开始检验", "Start Check", "검사 시작");
        Lang.Add("开始评级", "Start Rating", "등급 산정 시작");
        Lang.Add("取消检验", "Cancel Check", "검사 취소");
        Lang.Add("导出", "Export", "내보내기");
        Lang.Add("导出中…", "Exporting…", "내보내는 중…");
        Lang.Add("评级中…", "Rating…", "산정 중…");
        Lang.Add("检验进行中喵", "Check in progress", "검사 진행 중");
        Lang.Add("检验已取消喵", "Check cancelled", "검사 취소됨");
        Lang.Add("检验已停止喵", "Check stopped", "검사 중지됨");
        Lang.Add("重复检验中…", "Checking duplicates…", "중복 검사 중…");
        Lang.Add("正在扫描文件夹…", "Scanning folder…", "폴더 검사 중…");
        Lang.Add("本次没有可批量评级的谱面喵", "No charts to rate this time", "이번에는 등급을 매길 채보가 없어요");
        Lang.Add("该谱面暂不符合自动评级喵", "This chart can't be auto-rated yet", "이 채보는 아직 자동 등급을 매길 수 없어요");
        Lang.Add("选择要扫描的文件夹（含 .adofai 的子文件夹都会导入）",
            "Pick a folder to scan (subfolders containing .adofai will all be imported)",
            "검사할 폴더를 선택하세요 (.adofai가 있는 하위 폴더도 모두 가져옵니다)");
        Lang.Add("上下切换固定为 ↑ / ↓，不可修改",
            "Previous/Next is fixed to ↑ / ↓ and cannot be changed",
            "위/아래 전환은 ↑ / ↓ 로 고정되어 변경할 수 없습니다");
        Lang.Add("↑ / ↓（固定）", "↑ / ↓ (fixed)", "↑ / ↓ (고정)");

        // ---------- 搜索 / 列表 / 详情区 ----------
        Lang.Add("搜索谱面", "Search charts", "채보 검색");
        Lang.Add("搜索月正向山倾", "Search 月正向山倾", "月正向山倾 검색");
        Lang.Add("★ 收藏", "★ Favorites", "★ 즐겨찾기");
        // 库页"切换排序方式"的键：键上写的是当前排序方式
        Lang.Add("字母排序", "Sort: A-Z", "정렬: A-Z");
        Lang.Add("难度排序", "Sort: Rank", "정렬: 난이도");
        // 难度排序时" 收藏"与按等级排的那段之间那道隔断上的字
        Lang.Add("列表", "List", "목록");
        // 设置 → 工具 → 更新 里那个按钮（旁边的「开始检查」已有译文）
        Lang.Add("检查资源包完整性", "Check resource pack", "리소스 팩 검사");
        // 空库（第一次装 mod）那块的两个导入按钮 + 提示行
        Lang.Add("单个导入", "Import one", "하나 가져오기");
        Lang.Add("批量导入", "Import many", "여러 개 가져오기");
        Lang.Add("还没有谱面", "No charts yet", "아직 채보가 없어요");
        Lang.Add("没有匹配的谱面", "No matching charts", "일치하는 채보가 없습니다");
        // 空结果行：筛空那行是"清空筛选条件"的出口，必须与源码里的字面量逐字一致
        Lang.Add("还没有谱面，去 mod 设置页导入吧",
                 "No charts yet — import some from the mod settings page",
                 "아직 채보가 없어요 — 모드 설정 페이지에서 가져오세요");
        Lang.Add("没有匹配的谱面 · 点这里清空筛选条件",
                 "No matching charts · click here to clear the filters",
                 "일치하는 채보가 없어요 · 여기를 눌러 필터를 비우세요");
        Lang.Add("谱面信息", "Chart Info", "채보 정보");
        Lang.Add("选择关卡", "Select Level", "레벨 선택");
        Lang.Add("通关记录", "Clear Records", "클리어 기록");
        Lang.Add("寄录图", "Death Map", "사망 지도");
        Lang.Add("寄录图数据", "Death Map Data", "사망 지도 데이터");
        Lang.Add("数据管理", "Data Manager", "데이터 관리");
        Lang.Add("完成度数据", "Progress Data", "진행도 데이터");
        Lang.Add("谱面作者：", "Charter: ", "제작자: ");
        Lang.Add("曲师：", "Artist: ", "아티스트: ");
        Lang.Add("音乐名称：", "Song: ", "곡명: ");
        Lang.Add("音乐时长：", "Duration: ", "길이: ");
        Lang.Add("音乐长度：", "Song Length: ", "곡 길이: ");
        Lang.Add("基础 BPM：", "Base BPM: ", "기본 BPM: ");
        Lang.Add("格子数：", "Tiles: ", "타일 수: ");
        Lang.Add("难度：", "Difficulty: ", "난이도: ");
        Lang.Add("通关 ", "Clears ", "클리어 ");
        Lang.Add("游玩次数", "Play Count", "플레이 횟수");
        Lang.Add("总寄次数：", "Total Deaths: ", "총 사망 횟수: ");
        Lang.Add("正在播放：", "Now Playing: ", "재생 중: ");
        Lang.Add("开始游玩", "Start Playing", "플레이 시작");
        Lang.Add("确认进入编辑器", "Confirm Open Editor", "에디터 열기 확인");
        Lang.Add("打开关卡编辑器5秒后触发", "Triggers 5 seconds after opening the level editor",
            "레벨 에디터를 연 뒤 5초 후에 실행됩니다");
        Lang.Add("首次打开关卡编辑器打开谱面", "Open a chart in the level editor for the first time",
            "레벨 에디터에서 처음으로 채보를 열기");
        Lang.Add("还没有通关记录喵", "No clear records yet", "아직 클리어 기록이 없어요");
        Lang.Add("还没有寄录，去玩一局吧喵", "No death map yet — go play a run!", "아직 사망 지도가 없어요 — 한 판 해보세요!");
        Lang.Add("← 点击右边列表选择一个谱面，", "← Click a chart in the list on the right,",
            "← 오른쪽 목록에서 채보를 선택하세요,");
        Lang.Add("这里会显示它的详细信息喵", "its details will show up here", "여기에 자세한 정보가 표시됩니다");

        // ---------- 列表行 / 右键菜单动作 ----------
        Lang.Add("重命名", "Rename", "이름 변경");
        Lang.Add("标记", "Mark", "표시");
        Lang.Add("打开位置", "Open Folder", "폴더 열기");
        Lang.Add("删除", "Delete", "삭제");
        Lang.Add("打开", "Open", "열기");
        Lang.Add("关闭显示", "Hide", "숨기기");
        Lang.Add("更换头像", "Change Avatar", "아바타 변경");
        Lang.Add("更换名字", "Change Name", "이름 변경");

        // ---------- 通用按钮 ----------
        Lang.Add("确定", "OK", "확인");
        Lang.Add("取消", "Cancel", "취소");
        Lang.Add("关闭", "Close", "닫기");
        Lang.Add("知道了~", "Got it~", "알겠어요~");
        Lang.Add("知道啦！", "Got it!", "알겠어!");
        Lang.Add("复制", "Copy", "복사");
        Lang.Add("开", "On", "켜짐");
        Lang.Add("关", "Off", "꺼짐");
        Lang.Add("开/关", "On/Off", "켜기/끄기");
        Lang.Add("未设置", "Not set", "설정 안 됨");
        Lang.Add("为空", "Empty", "비어 있음");
        Lang.Add("（无）", "(none)", "(없음)");
        Lang.Add("（无内容）", "(no content)", "(내용 없음)");
        Lang.Add("（暂无数据）", "(no data)", "(데이터 없음)");
        Lang.Add("（点击修改）", " (click to change)", " (클릭해서 변경)");
        Lang.Add("已设置：", "Set to: ", "설정됨: ");
        Lang.Add("特殊", "Special", "특수");
        Lang.Add("未开始", "Not started", "시작 안 함");
        Lang.Add("未缩放", "Not scaled", "크기 조정 안 됨");
        Lang.Add("已销毁", "Destroyed", "삭제됨");
        Lang.Add("直接就位", "Snap into place", "즉시 배치");
        Lang.Add("空格", "Space", "스페이스");
        Lang.Add("回车", "Enter", "엔터");
        Lang.Add("一级", "Tier 1", "1급");
        Lang.Add("二级", "Tier 2", "2급");
        Lang.Add("三级", "Tier 3", "3급");
        Lang.Add("四级", "Tier 4", "4급");
        Lang.Add("五级", "Tier 5", "5급");

        // ---------- 导入 / 删除 / 重命名流程 ----------
        Lang.Add("导入单个谱面", "Import One Chart", "채보 하나 가져오기");
        Lang.Add("批量导入谱面", "Batch Import Charts", "채보 일괄 가져오기");
        Lang.Add("选择谱面", "Choose Chart", "채보 선택");
        Lang.Add("选择头像", "Choose Avatar", "아바타 선택");
        Lang.Add("选择头像图片", "Choose Avatar Image", "아바타 이미지 선택");
        Lang.Add("选择背景图片", "Choose Background Image", "배경 이미지 선택");
        Lang.Add("选择标记", "Choose Mark", "표시 선택");
        Lang.Add("选择关卡文件", "Choose Level File", "레벨 파일 선택");
        Lang.Add("选择文件夹", "Choose Folder", "폴더 선택");
        Lang.Add("修改玩家名字", "Edit Player Name", "플레이어 이름 수정");
        Lang.Add("重命名谱面", "Rename Chart", "채보 이름 변경");
        Lang.Add("删除谱面", "Delete Chart", "채보 삭제");
        Lang.Add("删除文件", "Delete File", "파일 삭제");
        Lang.Add("同时删除本地文件", "Also delete local files", "로컬 파일도 함께 삭제");
        Lang.Add("清空寄录图", "Clear Death Map", "사망 지도 비우기");
        // 清空筛选：卡死时的出口
        Lang.Add("清空", "Clear", "비우기");
        Lang.Add("清空谱面筛选", "Clear chart filters", "채보 필터 비우기");
        Lang.Add("已清空搜索条件与难度筛选",
                 "Search and difficulty filters cleared",
                 "검색과 난이도 필터를 비웠어요");
        Lang.Add("清空游玩记录", "Clear Play Records", "플레이 기록 비우기");
        Lang.Add("清除标记", "Clear Mark", "표시 지우기");
        Lang.Add("设为背景音乐", "Set as BGM", "배경음으로 설정");
        Lang.Add("确认导入", "Confirm Import", "가져오기 확인");
        Lang.Add("确定清空", "Confirm Clear", "비우기 확인");
        Lang.Add("确定删除", "Confirm Delete", "삭제 확인");
        Lang.Add("确定清空所有谱面的寄录？", "Clear the death map of every chart?", "모든 채보의 사망 지도를 비울까요?");
        Lang.Add("确定清空所有谱面的游玩次数和完成度？\n（游玩次数/完成度全部归零，通关记录/寄录图保留）",
            "Clear play counts and progress for every chart?\n(play counts and progress reset to zero; clear records and death maps are kept)",
            "모든 채보의 플레이 횟수와 진행도를 비울까요?\n(플레이 횟수/진행도는 0이 되고, 클리어 기록/사망 지도는 유지됩니다)");
        Lang.Add("确定删除谱面？\n", "Delete this chart?\n", "이 채보를 삭제할까요?\n");
        Lang.Add("确定删除 ", "Confirm delete ", "삭제 확인 ");
        Lang.Add("清空操作不可恢复，请谨慎喵", "Clearing cannot be undone — be careful!", "비우기는 되돌릴 수 없어요 — 조심하세요!");
        Lang.Add("将自动删除重复谱面（保留主谱面）", "Duplicate charts will be deleted automatically (the main one is kept)",
            "중복 채보는 자동으로 삭제됩니다 (원본은 유지)");
        Lang.Add("开始检验？", "Start the check?", "검사를 시작할까요?");
        Lang.Add("手动标记数据.txt", "manual_marks.txt", "수동_표시_데이터.txt");
        Lang.Add("文件夹里没有 .adofai 文件", "No .adofai file in the folder", "폴더에 .adofai 파일이 없습니다");
        Lang.Add("没有 .adofai 文件", "No .adofai file", ".adofai 파일이 없습니다");
        Lang.Add("没有可打开的 .adofai 文件", "No openable .adofai file", "열 수 있는 .adofai 파일이 없습니다");
        Lang.Add("谱面文件不存在：\n", "Chart file not found:\n", "채보 파일이 없습니다:\n");
        Lang.Add("找不到谱面文件喵", "Chart file not found", "채보 파일을 찾을 수 없어요");
        Lang.Add("文件缺失", "File missing", "파일 없음");
        Lang.Add("备份目录不存在喵", "Backup folder not found", "백업 폴더가 없어요");
        Lang.Add("备份位置未变化喵", "Backup location unchanged", "백업 위치가 그대로예요");
        Lang.Add("备份已移动到新位置喵", "Backup moved to the new location", "백업을 새 위치로 옮겼어요");
        Lang.Add("移动备份失败喵", "Failed to move the backup", "백업 이동에 실패했어요");
        Lang.Add("图片文件", "Image files", "이미지 파일");
        Lang.Add("ADOFAI 谱面", "ADOFAI charts", "ADOFAI 채보");
        Lang.Add("头像已更换喵", "Avatar updated", "아바타를 바꿨어요");
        Lang.Add("头像保存失败喵", "Failed to save the avatar", "아바타 저장에 실패했어요");
        Lang.Add("名字已保存喵", "Name saved", "이름을 저장했어요");
        Lang.Add("背景已更换喵", "Background updated", "배경을 바꿨어요");
        Lang.Add("背景已更换喵（没备份成，用的是原图）", "Background updated (using the original file)", "배경을 바꿨어요 (원본 파일 사용)");
        Lang.Add("背景图片加载失败喵", "Failed to load the background image", "배경 이미지를 불러오지 못했어요");
        Lang.Add("已取消背景音乐喵", "BGM cleared", "배경음을 해제했어요");
        Lang.Add("已设为背景音乐喵", "Set as BGM", "배경음으로 설정했어요");
        Lang.Add("已导出到桌面\\手动标记数据.txt 喵", "Exported to Desktop\\manual_marks.txt", "바탕화면\\수동_표시_데이터.txt 로 내보냈어요");
        Lang.Add("已删除该条记录喵", "Record deleted", "해당 기록을 삭제했어요");
        Lang.Add("游玩记录已清空喵", "Play records cleared", "플레이 기록을 비웠어요");
        Lang.Add("寄录图已清空喵", "Death map cleared", "사망 지도를 비웠어요");
        Lang.Add("已关闭个人信息栏显示（可在设置里重新打开）", "Player info hidden (you can re-enable it in Settings)",
            "플레이어 정보를 숨겼어요 (설정에서 다시 켤 수 있어요)");
        Lang.Add("已取消批量导入", "Batch import cancelled", "일괄 가져오기를 취소했어요");
        Lang.Add("批量导入正在进行中…", "Batch import already running…", "일괄 가져오기가 진행 중이에요…");
        Lang.Add("批量导入完成，正在自动检验重复谱面…", "Batch import done, checking duplicates…",
            "일괄 가져오기 완료, 중복 채보 검사 중…");
        Lang.Add("重复检验完成：没有发现重复谱面喵", "Duplicate check done: no duplicates found",
            "중복 검사 완료: 중복 채보가 없어요");
        Lang.Add("没有发现重复谱面喵", "No duplicate charts found", "중복 채보를 찾지 못했어요");
        Lang.Add("谱面重复：", "Duplicate chart: ", "중복 채보: ");
        Lang.Add("已有等级标记喵", "Already has a rank mark", "이미 등급 표시가 있어요");
        Lang.Add("请先选中一个谱面喵", "Select a chart first", "먼저 채보를 선택하세요");
        Lang.Add("请先在列表选中一个谱面", "Select a chart in the list first", "먼저 목록에서 채보를 선택하세요");
        Lang.Add("请按一个键…", "Press a key…", "키를 누르세요…");
        Lang.Add("请按键…", "Press a key…", "키를 누르세요…");
        Lang.Add("请按键盘上一个键…", "Press any key on the keyboard…", "키보드에서 아무 키나 누르세요…");
        Lang.Add("按键盘上任意一个键喵", "Press any key on the keyboard", "키보드에서 아무 키나 누르세요");
        Lang.Add("写点备注…", "Add a note…", "메모를 남겨보세요…");
        Lang.Add("通关记录", "Clear Records", "클리어 기록");
        Lang.Add("未设置", "Not set", "설정 안 됨");
        Lang.Add("宽判", "Lenient", "관대");
        Lang.Add("标判", "Normal", "보통");
        Lang.Add("严判", "Strict", "엄격");
        Lang.Add("玩家名字", "Player Name", "플레이어 이름");
        Lang.Add("创作者", "Creator", "제작자");
        Lang.Add("洛星橙", "洛星橙", "洛星橙");
        Lang.Add("云落落", "云落落", "云落落");
        Lang.Add("系机人喵", "系机人喵", "系机人喵");
        // 「母鸡先生」「月正向山倾」「一片烂海苔」新版制作名单里还在用，别跟着旧开发者页一起删
        Lang.Add("母鸡先生", "母鸡先生", "母鸡先生");
        Lang.Add("月正向山倾", "月正向山倾", "月正向山倾");
        Lang.Add("一片烂海苔", "一片烂海苔", "一片烂海苔");
        Lang.Add("月正", "月正", "月正");
        Lang.Add("月正NB", "月正NB", "月正NB");
        Lang.Add("微软雅黑", "Microsoft YaHei", "Microsoft YaHei");
        Lang.Add("副本", "copy", "복사본");
        Lang.Add("里程碑", "Milestone", "마일스톤");
        Lang.Add("里程碑谱面：", "Milestone chart: ", "마일스톤 채보: ");
        Lang.Add("触发条件：", "Unlock condition: ", "해금 조건: ");
        Lang.Add("解锁成就：", "Achievement unlocked: ", "업적 해금: ");
        Lang.Add("成就", "Achievements", "업적");
        Lang.Add("暂无数据", "No data", "데이터 없음");
        Lang.Add("历史数据", "History", "기록");
        Lang.Add("打卡！", "Checked in!", "체크인!");
        Lang.Add("每日打卡", "Daily Check-in", "일일 체크인");
        Lang.Add("每日学习", "Daily Study", "일일 학습");
        Lang.Add("今日打谱大学习", "Today's Chart Study", "오늘의 채보 공부");
        Lang.Add("热度上升", "Heating Up", "인기 상승");
        Lang.Add("正在播放", "Now Playing", "재생 중");
        Lang.Add("本次没有可批量评级的谱面喵", "No charts to rate this time", "이번에는 등급을 매길 채보가 없어요");
    }

    /// <summary>第二批：报错 / 提示 / 成就名与条件</summary>
    private static void Batch2() {
        // ---------- 报错弹窗与错误提示 ----------
        Lang.Add("出错了喵", "Something went wrong", "문제가 발생했어요");
        Lang.Add("下面的文字可以选中复制，反馈问题时贴给作者即可",
            "You can select and copy the text below — paste it to the author when reporting a problem.",
            "아래 글자를 선택해 복사한 뒤 제작자에게 보내주세요.");
        Lang.Add("游戏内主循环出现异常（界面可能已失效，建议重开一次）",
            "The main loop hit an error (the UI may be broken — please restart the game)",
            "메인 루프에서 오류가 발생했어요 (화면이 깨졌을 수 있으니 재시작을 권장합니다)");
        Lang.Add("报错弹窗自测：看到这个窗口说明报错接口工作正常",
            "Error dialog self-test: if you can see this window, the error API works",
            "오류 창 자체 테스트: 이 창이 보이면 오류 API가 정상입니다");
        Lang.Add("报错弹窗自测：如果你看到这个窗口，说明报错接口工作正常",
            "Error dialog self-test: if you see this window, the error API works",
            "오류 창 자체 테스트: 이 창이 보이면 오류 API가 정상입니다");
        Lang.Add("这是一条测试错误（按 F9 触发）", "This is a test error (triggered by F9)", "테스트 오류입니다 (F9로 실행)");
        Lang.Add("这是一条测试错误（由设置页「测试报错弹窗」触发）",
            "This is a test error (triggered by \"Test Error Popup\" in Settings)",
            "테스트 오류입니다 (설정의 \"오류 팝업 테스트\"로 실행)");
        Lang.Add("内层异常示例：文件被占用", "Inner exception example: file is in use", "내부 예외 예시: 파일이 사용 중");
        Lang.Add("错误信息已复制", "Error text copied", "오류 내용을 복사했어요");
        Lang.Add("复制错误信息失败", "Failed to copy the error text", "오류 내용 복사에 실패했어요");
        Lang.Add("打开右键菜单失败", "Failed to open the context menu", "우클릭 메뉴를 열지 못했어요");
        Lang.Add("打开个人信息栏菜单失败", "Failed to open the player info menu", "플레이어 정보 메뉴를 열지 못했어요");
        Lang.Add("创建批量进度条失败", "Failed to create the batch progress bar", "일괄 진행 표시줄을 만들지 못했어요");
        Lang.Add("打开批量导入确认框失败（导入已中止）", "Failed to open the batch import dialog (import aborted)",
            "일괄 가져오기 확인 창을 열지 못했어요 (가져오기 중단)");
        Lang.Add("保存设置失败（本次修改可能不会被记住）", "Failed to save settings (your change may not persist)",
            "설정 저장에 실패했어요 (변경 사항이 저장되지 않을 수 있어요)");
        Lang.Add("保存备注失败（备注可能丢失）", "Failed to save the note (it may be lost)", "메모 저장에 실패했어요 (사라질 수 있어요)");
        Lang.Add("保存标记失败（标记数据可能丢失）", "Failed to save marks (mark data may be lost)",
            "표시 저장에 실패했어요 (표시 데이터가 사라질 수 있어요)");
        Lang.Add("保存录入记录失败（下次启动可能漏掉已录入的谱面）",
            "Failed to save the record list (some charts may be missing next launch)",
            "기록 저장에 실패했어요 (다음 실행 때 일부 채보가 빠질 수 있어요)");
        Lang.Add("保存谱面显示名失败", "Failed to save the chart display name", "채보 표시 이름 저장에 실패했어요");
        Lang.Add("保存游玩次数失败", "Failed to save play counts", "플레이 횟수 저장에 실패했어요");
        Lang.Add("读取设置失败（将使用默认设置）", "Failed to read settings (using defaults)", "설정 읽기에 실패했어요 (기본값 사용)");
        Lang.Add("读取标记数据失败（标记可能显示为未标记，请检查 marks.txt 是否被占用或损坏）",
            "Failed to read mark data (marks may show as unmarked — check whether marks.txt is locked or damaged)",
            "표시 데이터 읽기에 실패했어요 (marks.txt가 잠겨 있거나 손상되었는지 확인하세요)");
        Lang.Add("删除后清理空目录失败", "Failed to clean up the empty folder after deleting", "삭제 후 빈 폴더 정리에 실패했어요");
        Lang.Add("刷新单行失败", "Failed to refresh the row", "행 새로고침에 실패했어요");
        Lang.Add("界面外玩家信息框更新失败", "Failed to update the outside player box", "패널 밖 플레이어 정보 갱신에 실패했어요");
        Lang.Add("设置页 CanvasGroup 丢失（设置页可能不可见）",
            "Settings page lost its CanvasGroup (it may be invisible)",
            "설정 페이지의 CanvasGroup이 없어졌어요 (보이지 않을 수 있어요)");
        Lang.Add("批量导入：扫描阶段出现未预期的错误", "Batch import: unexpected error while scanning",
            "일괄 가져오기: 검사 중 예상치 못한 오류");
        Lang.Add("批量导入：遍历子目录失败，可能有谱面被漏掉\n",
            "Batch import: failed to walk a subfolder — some charts may be skipped\n",
            "일괄 가져오기: 하위 폴더 탐색 실패 — 일부 채보가 빠질 수 있어요\n");
        Lang.Add("批量导入：扫描根目录失败 ", "Batch import: failed to scan the root folder ",
            "일괄 가져오기: 루트 폴더 검사 실패 ");
        Lang.Add("批量导入：扫描子目录失败 ", "Batch import: failed to scan a subfolder ",
            "일괄 가져오기: 하위 폴더 검사 실패 ");
        Lang.Add("，失败 {fail} 个（文件可能被占用）", ", {fail} failed (files may be in use)",
            ", {fail}개 실패 (파일이 사용 중일 수 있어요)");
        Lang.Add("「{(tab == 1 ? \"设置\" : tab == 2 ? \"开发者\" : \"库\")}」页面失败，已回退到库页（最后完成的步骤: {_buildStep}）",
            "Failed to build the page, fell back to the library (last step: {_buildStep})",
            "페이지 생성 실패, 라이브러리로 돌아갔어요 (마지막 단계: {_buildStep})");
        Lang.Add("吗？", "?", "?");
        Lang.Add("喵", " meow", " 냥");
        Lang.Add("次", " times", "회");
        Lang.Add("次）", " times)", "회)");
        Lang.Add("峰值 ", "Peak ", "최고 ");
        Lang.Add("按 F9 触发", "triggered by F9", "F9로 실행");
    }
}
