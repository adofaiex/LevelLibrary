namespace LevelLibrary;

/// <summary>多语言词条表 · 第四批：界面文案（含拼接串里的固定段）。
/// 拼接出来的文本整串查不到词条（Lang.Tr 会原样返回中文），所以固定段单独收进来、
/// 代码那边把固定段交给 Lang.Tr、动态值接在后面；动态内容本身不翻。
/// 键里的空格 / 全角空格 / 标点都是原文的一部分（如 "已解锁 " 末尾那个空格），别顺手删。</summary>
public static class LangData4 {
    /// <summary>界面文案（按分节收录：成就页、详情区、设置页等）。</summary>
    public static void Batch6() {
        // ---------- 成就页 / 提示 ----------
        Lang.Add("知道啦！", "Got it!", "알겠어!");
        Lang.Add("我已知晓（", "I understand (", "알겠습니다 (");
        Lang.Add("解锁成就：", "Achievement unlocked: ", "업적 해금: ");
        Lang.Add("搜索成就", "Search achievements", "업적 검색");
        Lang.Add("没有匹配的成就", "No matching achievements", "일치하는 업적이 없습니다");
        Lang.Add("已解锁 ", "Unlocked ", "해금 ");
        Lang.Add("　当前显示 ", "  showing ", "  표시 ");
        Lang.Add(" 张", " cards", " 장");
        Lang.Add("今日状态：", "Today: ", "오늘 상태: ");
        Lang.Add("已完成", "Done", "완료");
        Lang.Add("未完成", "Not done", "미완료");

        // ---------- 详情区 / 寄录图 / 列表 ----------
        Lang.Add("还没有通关记录喵", "No clear records yet meow", "아직 클리어 기록이 없어 냥");
        Lang.Add("没有 .adofai 文件", "No .adofai file", ".adofai 파일이 없습니다");
        Lang.Add("版本 ", "Version ", "버전 ");
        Lang.Add("版本 ?", "Version ?", "버전 ?");
        Lang.Add("请选择命名规则", "Choose a naming rule", "이름 규칙을 선택하세요");
        Lang.Add("谱面信息命名法", "Name from chart info", "채보 정보로 이름 짓기");
        Lang.Add("文件夹名称命名法", "Name from folder name", "폴더 이름으로 이름 짓기");
        Lang.Add("同时导入压缩包（.zip）", "Also extract archives (.zip)", "압축 파일도 함께 가져오기 (.zip)");
        Lang.Add("导入后备份谱面（会占用磁盘）", "Back up charts after import (uses disk space)",
                "가져온 후 채보 백업 (디스크 사용)");
        Lang.Add("导入后删原文件夹（不可恢复）", "Delete source folders after import (unrecoverable)",
                "가져온 후 원본 폴더 삭제 (복구 불가)");
        Lang.Add("确认", "Confirm", "확인");
        Lang.Add("取消", "Cancel", "취소");
        Lang.Add("检查并清理异常通关记录", "Check and clean abnormal clear records", "비정상 클리어 기록 확인 및 정리");
        Lang.Add("只清理这四种没有争议的记录：\n", "Only these four undisputed kinds are cleaned:\n",
                "논란이 없는 네 가지만 정리합니다:\n");
        Lang.Add("删除所有谱面", "Delete all charts", "모든 채보 삭제");
        Lang.Add("这会删除：\n", "This will delete:\n", "다음이 삭제됩니다:\n");
        Lang.Add("确认删除（", "Confirm delete (", "삭제 확인 (");
        Lang.Add("正在删除 ", "Deleting ", "삭제 중 ");
        Lang.Add("正在删除谱面数据 ", "Deleting chart data ", "채보 데이터 삭제 중 ");
        Lang.Add("正在刷新列表 …", "Refreshing the list...", "목록 새로 고치는 중 …");
        Lang.Add("确认导入", "Confirm import", "가져오기 확인");
        Lang.Add("开始检验？", "Start the check?", "검사를 시작할까요?");
        Lang.Add("将自动删除重复谱面（保留主谱面）", "Duplicate charts will be deleted (the original is kept)",
                "중복 채보는 자동 삭제됩니다 (원본은 유지)");
        Lang.Add("重复检验中…", "Checking for duplicates...", "중복 검사 중…");
        Lang.Add("谱面文件不存在：\n", "Chart file not found:\n", "채보 파일이 없습니다:\n");
        Lang.Add("出错了喵", "Something went wrong meow", "문제가 생겼어 냥");
        Lang.Add("下面的文字可以选中复制，反馈问题时贴给作者即可",
                "The text below can be selected and copied — paste it to the author when reporting",
                "아래 글을 선택해 복사한 뒤 제작자에게 붙여넣어 주세요");
        Lang.Add("复制", "Copy", "복사");
        Lang.Add("确定", "OK", "확인");
        Lang.Add("指令输入", "Command input", "명령 입력");

        // ---------- 大肥鱼设置 ----------
        Lang.Add("显示宠物", "Show pet", "펫 표시");
        Lang.Add("全局显示", "Show everywhere", "전역 표시");
        Lang.Add("游玩时隐藏", "Hide while playing", "플레이 중 숨기기");
        Lang.Add("显示气泡", "Show bubble", "말풍선 표시");
        Lang.Add("模型", "Model", "모델");
        // 内建的第二个模型（啄木鸟Tiper）已经改成可导入的 .llmd 包，这两条留着是为了让玩家导入的那份
        // 在英 / 韩下也能翻（包的 name= 与台词原文就是这两句）
        Lang.Add("啄木鸟Tiper", "Tiper the woodpecker", "딱따구리 Tiper");
        Lang.Add("导入模型", "Import model", "모델 가져오기");
        Lang.Add("选择模型文件", "Choose model file", "모델 파일 선택");
        // 系统文件对话框的标题与筛选名（跟「选择视频背景 / 视频文件」同一套做法）
        Lang.Add("选择宠物模型包", "Choose pet model pack", "펫 모델 팩 선택");
        Lang.Add("宠物模型包", "Pet model pack", "펫 모델 팩");
        Lang.Add("宠物大小", "Pet size", "펫 크기");
        Lang.Add("音效", "Sound", "효과음");
        Lang.Add("音量", "Volume", "볼륨");
        Lang.Add("自动吸附", "Auto snap", "자동 붙기");
        Lang.Add("重置位置与大小", "Reset position and size", "위치와 크기 초기화");

        // ---------- 列表 / 详情 / 右键 ----------
        Lang.Add("重命名谱面", "Rename chart", "채보 이름 바꾸기");
        Lang.Add("评级中…", "Rating...", "등급 산정 중…");
        Lang.Add("导出中…", "Exporting...", "내보내는 중…");
        Lang.Add("完成度：", "Completion: ", "진행도: ");
        Lang.Add("还没有寄录，去玩一局吧喵", "No death map yet — go play a round meow",
                "아직 사망 기록이 없어, 한 판 해보자 냥");
        Lang.Add("峰值 ", "Peak ", "최고 ");
        Lang.Add(" 次", " times", " 회");
        Lang.Add("总寄次数：", "Total deaths: ", "총 사망 횟수: ");
        Lang.Add("子关卡", "Sub-levels", "하위 레벨");
        Lang.Add("完成度 0%   ←   死亡分布   →   100%", "0%   ←   Death distribution   →   100%",
                "0%   ←   사망 분포   →   100%");
        Lang.Add("打开谱面：", "Open chart: ", "채보 열기: ");
        Lang.Add("打开随机谱面：", "Open random chart: ", "랜덤 채보 열기: ");
        Lang.Add("向上切换：", "Previous: ", "위로: ");
        Lang.Add("向下切换：", "Next: ", "아래로: ");
        Lang.Add("曲师：", "Artist: ", "아티스트: ");
        Lang.Add("确定删除谱面？\n", "Delete this chart?\n", "이 채보를 삭제할까요?\n");
        Lang.Add("选择关卡文件", "Choose a level file", "레벨 파일 선택");
        Lang.Add("谱面信息", "Chart info", "채보 정보");
        Lang.Add("难度：", "Difficulty: ", "난이도: ");
        Lang.Add("写点备注…", "Write a note...", "메모를 적어보세요…");
        Lang.Add("文件夹里没有 .adofai 文件", "No .adofai file in the folder", "폴더에 .adofai 파일이 없습니다");
        Lang.Add("数据管理", "Data management", "데이터 관리");
        Lang.Add("清空操作不可恢复，请谨慎喵", "Clearing cannot be undone, be careful meow",
                "초기화는 되돌릴 수 없어, 조심하자 냥");
        Lang.Add("音乐名称：", "Song name: ", "음악 이름: ");
        Lang.Add("通关记录", "Clear records", "클리어 기록");
        Lang.Add("搜索谱面", "Search charts", "채보 검색");
        Lang.Add("还没有谱面", "No charts yet", "아직 채보가 없습니다");
        Lang.Add("确定删除 ", "Delete ", "삭제할까요 ");
        Lang.Add(" 吗？", "?", " ?");

        // ---------- 个人页 / 周报 / 生日 ----------
        Lang.Add("登录", "Log in", "로그인");
        Lang.Add("当当当当！！", "Ta-da!!", "짜잔!!");
        Lang.Add("今天是该 MOD 的作者 洛星橙 的生日噢～\n\n",
                "Today is the birthday of this mod's author, Luo Xingcheng~\n\n",
                "오늘은 이 모드 제작자 낙성귤의 생일이에요~\n\n");
        Lang.Add("知道了~", "Got it~", "알겠어~");
        Lang.Add("周报", "Weekly Report", "주간 리포트");
        Lang.Add("本周数据", "Weekly Stats", "주간 통계");
        Lang.Add("本周称号", "Weekly Title", "주간 칭호");
        Lang.Add("历史数据", "History", "과거 데이터");
        Lang.Add("这一周也很努力啦\n下一周也要开开心心的过呀～",
                "You worked hard this week\nHave a happy week ahead too~",
                "이번 주도 열심히 했어\n다음 주도 즐겁게 보내자~");
        Lang.Add("（暂无数据）", "(No data yet)", "(데이터 없음)");
        Lang.Add("修改玩家名字", "Change player name", "플레이어 이름 변경");
        Lang.Add("在这里输入要滚动显示的文字…", "Type the text to scroll here...", "여기에 스크롤할 글자를 입력하세요…");

        // ---------- 设置页（第一批没覆盖到的） ----------
        Lang.Add("使用 LevelLibrary 接管进入", "Let LevelLibrary take over on launch", "게임 시작 시 LevelLibrary가 진입 담당");
        Lang.Add("开机自动进关卡编辑器并打开谱面库，同时隐去 UMM 的模组列表弹窗",
            "Opens the level editor, then the chart library, and hides the UMM mod-list popup",
            "레벨 에디터와 채보 라이브러리를 자동으로 열고 UMM 모드 목록 팝업을 숨깁니다");
        Lang.Add("开始界面作为屏保", "Start screen as screensaver", "시작 화면을 화면 보호기로");
        Lang.Add("屏保休眠时间", "Screensaver idle time", "화면 보호기 대기 시간");
        Lang.Add("屏保全局显示", "Screensaver everywhere", "화면 보호기 전역 표시");
        Lang.Add("导入单个谱面", "Import a single chart", "채보 하나 가져오기");
        Lang.Add("自动导入", "Auto import", "자동 가져오기");
        Lang.Add("自动导入后备份", "Back up auto-imported charts", "자동 가져오기 후 백업");
        Lang.Add("游玩时长：{0}", "Play time: {0}", "플레이 시간: {0}");
        Lang.Add("视频换不上去喵（文件被占用，重启游戏再试一次）",
                "Couldn't swap in the video (the file is in use, restart the game and try again)",
                "동영상을 교체하지 못했어요 (파일이 사용 중이에요, 게임을 다시 시작해 주세요)");
        Lang.Add("谱面导入后备份", "Back up after import", "가져온 후 백업");
        Lang.Add("自动标记", "Auto mark", "자동 표시");
        Lang.Add("自定义备份位置", "Custom backup folder", "백업 폴더 지정");
        Lang.Add("打开备份位置", "Open backup folder", "백업 폴더 열기");
        Lang.Add("清空谱面筛选", "Clear chart filters", "채보 필터 초기화");
        Lang.Add("选择头像", "Choose avatar", "아바타 선택");
        Lang.Add("玩家名字", "Player name", "플레이어 이름");
        Lang.Add("显示个人信息栏", "Show player info", "플레이어 정보 표시");
        Lang.Add("界面外显示", "Show outside panel", "패널 밖에도 표시");
        Lang.Add("X 位置", "X position", "X 위치");
        Lang.Add("Y 位置", "Y position", "Y 위치");
        Lang.Add("自定义背景", "Custom background", "커스텀 배경");
        Lang.Add("主题色", "Theme color", "테마 색");
        Lang.Add("视频背景", "Video background", "동영상 배경");
        Lang.Add("选择视频", "Choose video", "동영상 선택");
        Lang.Add("背景音乐音量", "BGM volume", "배경음 볼륨");
        Lang.Add("界面字体大小", "Interface font size", "인터페이스 글자 크기");
        Lang.Add("谱面列表字体大小", "Chart list font size", "채보 목록 글자 크기");
        Lang.Add("成就名称字体大小", "Achievement name font size", "업적 이름 글자 크기");
        Lang.Add("成就详细信息字体大小", "Achievement detail font size", "업적 상세 글자 크기");
        Lang.Add("面板背景模糊", "Panel background blur", "패널 배경 흐림");
        Lang.Add("显示滚动栏", "Show now-playing bar", "스크롤 바 표시");
        Lang.Add("显示内容", "Content", "표시 내용");
        Lang.Add("自定义信息", "Custom text", "커스텀 문구");
        Lang.Add("打开选中谱面", "Open selected chart", "선택한 채보 열기");
        Lang.Add("随机谱面并打开", "Open a random chart", "랜덤 채보 열기");
        Lang.Add("向上切换 / 向下切换", "Previous / Next", "위로 / 아래로 전환");
        Lang.Add("记录详细日志", "Verbose logging", "상세 로그 기록");
        Lang.Add("测试报错弹窗", "Test error popup", "오류 팝업 테스트");
        Lang.Add("检查界面字体", "Check UI font", "UI 글꼴 검사");
        Lang.Add("重复谱面检验", "Duplicate check", "중복 채보 검사");
        Lang.Add("比对每个谱面 adofai 文件的内容（哈希），内容完全一致的视为重复。\n检验完成后会自动删除重复谱面（保留主谱面，目录名带「副本/copy」的优先被删）。",
                "Compares the contents (hash) of every chart's adofai file; identical ones count as duplicates.\nAfterwards duplicates are deleted automatically (the original is kept; folders named 「copy」 go first).",
                "모든 채보의 adofai 파일 내용(해시)을 비교해 완전히 같은 것을 중복으로 봅니다.\n검사가 끝나면 중복 채보는 자동 삭제됩니다 (원본 유지, 폴더 이름에 「copy」가 있으면 우선 삭제).");
        Lang.Add("社区评级", "Community rating", "커뮤니티 등급");
        Lang.Add("导出手动标记数据", "Export manual marks", "수동 표시 데이터 내보내기");
        Lang.Add("异常通关记录", "Abnormal clear records", "비정상 클리어 기록");
        Lang.Add("删除所有谱面：清掉备份/导入的全部谱面文件和谱面列表，以及这些谱面的数据\n",
                "Delete all charts: clears every backed-up/imported chart file and the chart list, plus their data\n",
                "모든 채보 삭제: 백업/가져온 채보 파일과 목록, 그리고 그 채보들의 데이터를 지웁니다\n");
        Lang.Add("此功能基于 AutoRanting 与一元线性回归模型进行开发，使用最小二乘法估计经验回归直线。\n",
                "This feature is built on AutoRanting and a simple linear regression model, using least squares to estimate the empirical line.\n",
                "이 기능은 AutoRanting과 단순 선형 회귀 모델을 기반으로 하며, 최소제곱법으로 경험 회귀선을 추정합니다.\n");
        Lang.Add("自动检查更新", "Check for updates automatically", "자동 업데이트 확인");
        Lang.Add("检查更新", "Check for updates", "업데이트 확인");
        Lang.Add("打开 Mod 页面", "Open the mod page", "모드 페이지 열기");
        Lang.Add("检查资源包完整性", "Check asset pack integrity", "리소스 팩 무결성 검사");
        Lang.Add("mod 版本：", "Mod version: ", "모드 버전: ");
        Lang.Add("月正NB关注月正谢谢喵喵喵\n感谢月正大人的帮助喵，谢谢喵",
                "Yuezheng is awesome, follow Yuezheng, thank you meow meow meow\nThanks to Lord Yuezheng for the help, thank you meow",
                "월정 짱, 월정 팔로우 부탁해요 냥냥냥\n월정님 도움 감사합니다 냥, 고마워요 냥");
    }

    /// <summary>拼接串的 {0} 模板（Lang.TrFmt 用）。
    /// 这些键带 {0}，TextTranslatePatch 的 ShouldSkip 会跳过它们；没关系，显示时它已经是 TrFmt 翻好的成品。</summary>
    public static void Batch7() {
        Lang.Add("我已知晓", "I understand", "알겠습니다");
        Lang.Add("我已知晓（{0}）", "I understand ({0})", "알겠습니다 ({0})");
        Lang.Add("已解锁 {0} / {1}　当前显示 {2} 张", "Unlocked {0} / {1}  showing {2}",
                "해금 {0} / {1}  표시 {2}");
        Lang.Add("达成时间：", "Unlocked at: ", "달성 시각: ");
        Lang.Add("达成时间：未达成", "Unlocked at: not yet", "달성 시각: 미달성");
        Lang.Add("确认清理 ", "Confirm cleaning ", "정리 확인 ");
        Lang.Add(" 条", " records", " 개");
        Lang.Add("没有要清理的", "Nothing to clean", "정리할 것이 없습니다");
        Lang.Add("确认删除", "Confirm delete", "삭제 확인");
        Lang.Add("确认删除（{0}）", "Confirm delete ({0})", "삭제 확인 ({0})");
        Lang.Add("正在删除 {0} …", "Deleting {0} ...", "{0} 삭제 중 ...");
        Lang.Add("正在删除谱面数据 {0}/{1} …", "Deleting chart data {0}/{1} ...", "채보 데이터 삭제 중 {0}/{1} ...");
        Lang.Add("峰值 {0}% ~ {1}% · {2} 次", "Peak {0}% ~ {1}% · {2} times", "최고 {0}% ~ {1}% · {2} 회");
        Lang.Add("这一关共 ", "This level has ", "이 레벨은 총 ");
        Lang.Add(" 次死亡", " deaths", " 회 사망");
        Lang.Add("还没有寄录数据。", "No death map data yet.", "아직 사망 기록 데이터가 없습니다.");
        Lang.Add("游玩时长：{0} 小时 {1} 分", "Play time: {0} h {1} min", "플레이 시간: {0}시간 {1}분");
        Lang.Add("累计游玩次数：{0}", "Total plays: {0}", "누적 플레이 횟수: {0}");
        Lang.Add("确定删除 {0} 吗？", "Delete {0}?", "{0}을(를) 삭제할까요?");
        Lang.Add("欢迎回来，", "Welcome back, ", "돌아온 걸 환영해, ");
        Lang.Add("只清理这四种**没有争议**的记录：\n",
                "Only these four undisputed kinds are cleaned:\n",
                "논란이 없는 네 가지만 정리합니다:\n");
        // 判定图例六个词（详情页顶部那句带颜色的；富文本标签本身不算词条）
        Lang.Add("提前", "Early", "빠름");
        Lang.Add("太快", "Too Early", "너무 빠름");
        Lang.Add("稍快", "Slightly Early", "약간 빠름");
        Lang.Add("完美", "Perfect", "완벽");
        Lang.Add("稍慢", "Slightly Late", "약간 느림");
        Lang.Add("太慢", "Too Late", "너무 느림");
    }

    /// <summary>第五批：经变量 / 数组 / 参数传进去的文本 —— 它们都不是 .text = "中文" 那个形状，
    /// 而是先写进数组、或当参数传给 MenuAction / MakeFixedTab，所以必须在这里显式收进来。</summary>
    public static void Batch8() {
        // ---------- ① 右键菜单（谱面列表 / 个人信息栏 / 通关记录） ----------
        Lang.Add("重命名", "Rename", "이름 바꾸기");
        Lang.Add("标记", "Mark", "표시");
        Lang.Add("打开位置", "Open Location", "위치 열기");
        Lang.Add("删除", "Delete", "삭제");
        Lang.Add("设置背景音乐", "Set as BGM", "배경음으로 설정");
        Lang.Add("取消背景音乐", "Unset BGM", "배경음 해제");
        Lang.Add("导出 zip 至桌面", "Export zip to Desktop", "zip을 바탕화면으로 내보내기");
        Lang.Add("自动评级", "Auto Rate", "자동 등급 산정");
        Lang.Add("关闭显示", "Hide", "표시 끄기");
        Lang.Add("更换头像", "Change Avatar", "아바타 변경");
        Lang.Add("更换名字", "Change Name", "이름 변경");
        Lang.Add("删除此记录", "Delete This Record", "이 기록 삭제");

        // ---------- ③⑥ 顶层 tab + 成就左栏分类 ----------
        Lang.Add("官谱库", "Official Library", "공식 라이브러리");
        Lang.Add("成就", "Achievements", "업적");
        Lang.Add("P级里程碑", "P Milestones", "P 마일스톤");
        Lang.Add("G级里程碑", "G Milestones", "G 마일스톤");
        Lang.Add("U级里程碑", "U Milestones", "U 마일스톤");
        Lang.Add("官方成就", "Official Achievements", "공식 업적");
        Lang.Add("MOD成就", "Mod Achievements", "MOD 업적");
        Lang.Add("私人", "Personal", "개인");
        Lang.Add("彩蛋成就", "Easter Egg Achievements", "이스터에그 업적");

        // ---------- ③ 官谱库的区域 tab ----------
        Lang.Add("基础关卡", "Base Levels", "기본 레벨");
        Lang.Add("进阶关卡", "Advanced Levels", "심화 레벨");
        Lang.Add("附加关卡", "Extra Levels", "추가 레벨");
        Lang.Add("皇冠岛", "Crown Island", "크라운 아일랜드");
        Lang.Add("愚人节＆彩蛋", "April Fools & Easter Eggs", "만우절 & 이스터에그");

        // ---------- ⑤ 设置的主 tab / 子 tab ----------
        Lang.Add("常规", "General", "일반");
        Lang.Add("外观", "Appearance", "외형");
        Lang.Add("操作", "Controls", "조작");
        Lang.Add("工具", "Tools", "도구");
        Lang.Add("扩展", "Extensions", "확장");
        Lang.Add("启动", "Startup", "시작");
        Lang.Add("导入与存储", "Import & Storage", "가져오기와 저장");
        Lang.Add("背景与外观", "Background & Look", "배경과 외형");
        Lang.Add("滚动栏", "Now-Playing Bar", "스크롤 바");
        Lang.Add("诊断", "Diagnostics", "진단");
        Lang.Add("谱面检验", "Chart Checks", "채보 검사");
        Lang.Add("更新", "Updates", "업데이트");
        Lang.Add("大肥鱼", "Fat Fish", "뚱뚱이 물고기");

        // ---------- ④ 官谱库详情区 ----------
        Lang.Add("进入关卡", "Enter Level", "레벨 들어가기");
        Lang.Add("世界 {0} · {1} · 难度 {2}", "World {0} · {1} · Difficulty {2}",
                "월드 {0} · {1} · 난이도 {2}");
        Lang.Add(" · 试炼 ×{0}", " · Trial ×{0}", " · 시련 ×{0}");
        Lang.Add(" · 有检查点", " · Has checkpoints", " · 체크포인트 있음");
        Lang.Add(" · DLC", " · DLC", " · DLC");
        Lang.Add("世界 {0} · 游戏世界表里没有这一项（只有关卡本体）",
                "World {0} · not in the game's world table (level data only)",
                "월드 {0} · 게임 월드 표에 없는 항목입니다 (레벨 본체만)");
        Lang.Add("完成度：—\n尝试次数：—\n状态：游戏没给这个世界记数据",
                "Completion: —\nAttempts: —\nStatus: the game keeps no data for this world",
                "진행도: —\n시도 횟수: —\n상태: 게임이 이 월드의 데이터를 기록하지 않습니다");
        Lang.Add("最佳准确度：—\n最佳 X 准确度：—\n", "Best accuracy: —\nBest X accuracy: —\n",
                "최고 정확도: —\n최고 X 정확도: —\n");

        // ---------- ②⑧ 寄录图 / 成就进度（{0} 模板） ----------
        Lang.Add("\n最难点：{0}% ~ {1}% 处（{2} 次）", "\nHardest spot: {0}% ~ {1}% ({2} times)",
                "\n최고 난도 지점: {0}% ~ {1}% ({2} 회)");
        Lang.Add("（再 {0} 次升 {1} 级）", " ({0} more to level {1})", " ({0}회 더 하면 {1}레벨)");
        Lang.Add("成就等级 {0}（满级）　总经验 {1}", "Achievement level {0} (max)  Total EXP {1}",
                "업적 레벨 {0} (최대)  총 경험치 {1}");
        Lang.Add("成就等级 {0}　总经验 {1}", "Achievement level {0}  Total EXP {1}",
                "업적 레벨 {0}  총 경험치 {1}");
        Lang.Add("达成时间：？？？", "Unlocked at: ???", "달성 시각: ???");

        // ---------- 其余可见按钮 / 标题 / 标签 ----------
        Lang.Add("检查并清理", "Check and Clean", "확인 후 정리");
        Lang.Add("删除全部", "Delete All", "전부 삭제");
        Lang.Add("立即检查", "Check Now", "지금 확인");
        Lang.Add("打开网页", "Open Web Page", "웹 페이지 열기");
        Lang.Add("选择文件导入", "Choose File", "파일 선택");
        // 视频压缩挡位那套新界面的中文原文（作者加功能时漏进词条表，这里补齐：
        // 界面里一律不写裸中文，逐条的译文都收在这张表里）
        Lang.Add("单个导入接受压缩包", "Accept .zip in single import", "단일 가져오기에서 압축 파일 허용");
        Lang.Add("恢复默认视频", "Restore Default Video", "기본 동영상 복원");
        // 背景设置那一族新增的行标签 / 按钮（静态背景两份图 + 默认视频背景的恢复 / 下载两态）
        Lang.Add("静态背景", "Static Background", "정적 배경");
        // 大厅立绘（小界面那四行标签 + 选图按钮）
        Lang.Add("选择立绘图片", "Choose Portrait Image", "일러스트 이미지 선택");
        Lang.Add("旋转角度", "Rotation", "회전 각도");
        Lang.Add("X 位置", "X Position", "X 위치");
        Lang.Add("Y 位置", "Y Position", "Y 위치");
        Lang.Add("立绘大小", "Portrait Size", "일러스트 크기");
        Lang.Add("选择图片", "Choose Image", "이미지 선택");
        Lang.Add("默认视频背景", "Default Video Background", "기본 동영상 배경");
        Lang.Add("恢复默认视频背景", "Restore Default Video Background", "기본 동영상 배경 복원");
        Lang.Add("下载默认视频背景", "Download Default Video Background", "기본 동영상 배경 다운로드");
        Lang.Add("选择压缩挡位", "Choose Compression Level", "압축 등급 선택");
        Lang.Add("档位越高越清晰，也越吃解码性能与磁盘",
                 "Higher levels look sharper but cost more decoding power and disk space",
                 "등급이 높을수록 선명하지만 디코딩 성능과 디스크를 더 사용합니다");
        Lang.Add("（当前）", " (current)", " (현재)");
        // 大厅那排方图标与按钮群上新增的几处文案（界面里一律不写裸中文，逐条译文都收在这张表里）
        Lang.Add("反馈", "Feedback", "피드백");
        Lang.Add("数据", "Data", "데이터");
        Lang.Add("导入谱面", "Import", "채보 가져오기");
        Lang.Add("反馈与联系", "Feedback & Contact", "피드백 및 연락처");
        Lang.Add("知道了", "Got it", "알겠어요");
        // Orca 彩蛋（成就名/描述 + 大肥鱼三句台词 + 日志标签）
        Lang.Add("这是我家？！", "This is my home?!", "여기가 내 집이야?!");
        Lang.Add("打开 Orca（音乐 Orca.ogg、物量 848）时触发", "Open Orca (music Orca.ogg, 848 tiles)", "Orca(음악 Orca.ogg, 848 타일)를 열면 해금");
        Lang.Add("回家的路终于找到了", "Finally found the way home", "집에 가는 길을 드디어 찾았어요");
        Lang.Add("这条鱼可真肥，不对，这TM不是我吗！", "What a fat fish... wait, THAT'S ME?!", "고래가 진짜 통통하네, 아니, 이거 나잖아!");
        Lang.Add("到家咯！", "I'm home!", "집에 도착했어!");
        Lang.Add("Orca 第 420 砖", "Orca tile 420", "Orca 420번째 타일");
        Lang.Add("Orca 第 847 砖", "Orca tile 847", "Orca 847번째 타일");
        Lang.Add("Orca 进图", "Orca enter", "Orca 진입");
        Lang.Add("恢复默认背景", "Restore Default Background", "기본 배경 복원");
        Lang.Add("选择视频文件", "Choose Video File", "동영상 파일 선택");
        Lang.Add("选择视频背景", "Choose Video Background", "동영상 배경 선택");
        Lang.Add("视频文件", "Video files", "동영상 파일");
        Lang.Add("歌曲名", "Song name", "곡 이름");
        Lang.Add("开始", "Start", "시작");
        Lang.Add("重置", "Reset", "초기화");
        Lang.Add("删", "Del", "삭제");
        Lang.Add("输入指令后回车…", "Type a command and press Enter...", "명령을 입력하고 Enter...");
        Lang.Add("← 先选择一个谱面喵", "← Pick a chart first meow", "← 먼저 채보를 골라줘 냥");
        Lang.Add("清空通关记录", "Clear clear records", "클리어 기록 비우기");
        Lang.Add("清空游玩次数", "Clear play counts", "플레이 횟수 비우기");
        Lang.Add("只清当前选中的谱面，其它谱面不受影响", "Only the selected chart is cleared; other charts are untouched",
                 "선택한 채보만 지웁니다. 다른 채보는 그대로입니다");
        Lang.Add("导出中 {0}/{1}", "Exporting {0}/{1}", "내보내는 중 {0}/{1}");
        Lang.Add("评级中 {0}/{1}", "Rating {0}/{1}", "등급 산정 중 {0}/{1}");
        Lang.Add("游玩 {0} 次    完成度 {1}%", "{0} plays    {1}% complete", "{0}회 플레이    진행도 {1}%");
        Lang.Add("还有 {0} 条错误（点确定继续查看）", "{0} more errors (click OK to keep viewing)",
                "오류 {0}건 더 있음 (확인을 누르면 계속)");
        Lang.Add("本次运行共 {0} 条错误", "{0} errors this session", "이번 실행에서 오류 {0}건");
        Lang.Add("无文件", "No file", "파일 없음");

        // ---------- 开始界面 / 启动页 ----------
        Lang.Add("上滑进入", "Swipe up to enter", "위로 밀어 들어가기");
        Lang.Add("上滑关闭", "Swipe up to close", "위로 밀어 닫기");
        // 开始界面那行更新提示（更新器里那几句 Status 只进日志与轻提示，这一行是直接画在屏上的）
        Lang.Add("检查更新中（{0}/{1}）", "Checking for updates ({0}/{1})", "업데이트 확인 중 ({0}/{1})");
        Lang.Add("正在下载 mod 更新", "Downloading mod update", "mod 업데이트 다운로드 중");
        Lang.Add("正在下载 mod 更新（{0}%）", "Downloading mod update ({0}%)", "mod 업데이트 다운로드 중 ({0}%)");
        Lang.Add("正在下载资源包", "Downloading asset pack", "리소스 팩 다운로드 중");
        Lang.Add("正在下载资源包（{0}%）", "Downloading asset pack ({0}%)", "리소스 팩 다운로드 중 ({0}%)");
        Lang.Add("正在连接更新服务器，还没开始下载", "Connecting to update server, download hasn't started yet", "업데이트 서버에 연결 중, 아직 다운로드를 시작하지 않음");
        Lang.Add("正在安装 mod 更新（{0}%）", "Installing mod update ({0}%)", "mod 업데이트 설치 중 ({0}%)");
        Lang.Add("正在安装资源包（{0}%）", "Installing asset pack ({0}%)", "리소스 팩 설치 중 ({0}%)");
        Lang.Add("网络错误", "Network error", "네트워크 오류");
        Lang.Add("更新完成，请重启游戏", "Update complete, please restart the game",
                "업데이트 완료, 게임을 다시 시작해 주세요");
        Lang.Add("更新日志", "Changelog", "업데이트 내역");
        Lang.Add("公告", "Notice", "공지");
        Lang.Add("长按任意处回到谱面库", "Hold anywhere to return to the library",
                "아무 곳이나 길게 누르면 라이브러리로 돌아갑니다");
        Lang.Add("长按任意处关闭屏保", "Hold anywhere to close the screensaver",
                "아무 곳이나 길게 누르면 화면 보호기가 꺼집니다");
        Lang.Add("星期日", "Sunday", "일요일");
        Lang.Add("星期一", "Monday", "월요일");
        Lang.Add("星期二", "Tuesday", "화요일");
        Lang.Add("星期三", "Wednesday", "수요일");
        Lang.Add("星期四", "Thursday", "목요일");
        Lang.Add("星期五", "Friday", "금요일");
        Lang.Add("星期六", "Saturday", "토요일");

        // ---------- 周报称号（16 档，玩家看得见） ----------
        Lang.Add("咸鱼一条", "Total Slacker", "완전 잉여");
        Lang.Add("懒散开局", "Lazy Start", "느긋한 시작");
        Lang.Add("轻度摸鱼", "Light Slacking", "가벼운 딴짓");
        Lang.Add("悠闲玩家", "Casual Player", "여유로운 플레이어");
        Lang.Add("稳中求进", "Steady Progress", "꾸준한 발전");
        Lang.Add("小有所成", "Getting Somewhere", "조금씩 성과");
        Lang.Add("积极向前", "Moving Forward", "앞으로 나아가기");
        Lang.Add("手感渐热", "Warming Up", "감각이 올라오는 중");
        Lang.Add("不骄不躁", "Calm and Steady", "조급하지 않게");
        Lang.Add("持之以恒", "Persistent", "꾸준함");
        Lang.Add("勇攀高峰", "Climbing Higher", "높은 곳으로");
        Lang.Add("高难猎手", "Hard-Chart Hunter", "고난도 헌터");
        Lang.Add("爆肝达人", "Grind Machine", "노가다 장인");
        Lang.Add("全勤战神", "Perfect Attendance", "개근의 신");
        Lang.Add("极限征服", "Limit Conqueror", "한계 정복자");
        Lang.Add("巅峰传说", "Peak Legend", "정점의 전설");
        Lang.Add("传奇引擎", "Legendary Engine", "전설의 엔진");

        // ---------- 游玩时长（大肥鱼 HUD 三个标签）/ 判定名（记录页） ----------
        Lang.Add("今日游玩 60 分钟", "Played 60 min today", "오늘 60분 플레이");
        Lang.Add("今日游玩 300 分钟", "Played 300 min today", "오늘 300분 플레이");
        Lang.Add("今日游玩 600 分钟", "Played 600 min today", "오늘 600분 플레이");
        Lang.Add("太早", "Too Early", "너무 이름");
        Lang.Add("很早", "Very Early", "매우 이름");
        Lang.Add("太晚", "Too Late", "너무 늦음");
        Lang.Add("过晚", "Very Late", "매우 늦음");
        Lang.Add("失误", "Miss", "실수");
        Lang.Add("过载", "Overload", "과부하");
        Lang.Add("自动格", "Auto Tile", "자동 칸");
        Lang.Add("过按", "Overpress", "과입력");
        Lang.Add("好音乐", "Good music", "좋은 음악");
        // 插值字符串改用 TrFmt 之后新增的模板
        Lang.Add("扫描范围：{0}\n扫描到 {1} 个含谱面的文件夹\n确认要全部导入吗？\n(选错文件夹可点取消)",
                "Scanning: {0}\nFound {1} folders containing charts\nImport all of them?\n(cancel if you picked the wrong folder)",
                "검사 범위: {0}\n채보가 든 폴더 {1}개 발견\n전부 가져올까요?\n(폴더를 잘못 골랐다면 취소)");
        Lang.Add("还有 {0} 条错误（点确定继续查看）", "{0} more errors (click OK to keep viewing)",
                "오류 {0}건 더 있음 (확인을 누르면 계속)");
        Lang.Add("本次运行共 {0} 条错误", "{0} errors this session", "이번 실행에서 오류 {0}건");
        Lang.Add("谱面总数：{0}    通关数：{1}    总游玩数：{2}", "Charts: {0}    Cleared: {1}    Total plays: {2}",
                "채보: {0}    클리어: {1}    총 플레이: {2}");
        Lang.Add("  x{0}次", "  x{0} times", "  x{0}회");
        // 成就卡「详细信息」那一块是多行拼出来的，多行整串查不到词条，只能在拼装时逐行翻
        // （见 PortalUI.Achievements.Card.cs）；这里收的是它用到的键。
        Lang.Add("等级 {0}", "Level {0}", "레벨 {0}");
        Lang.Add("触发次数 {0}", "Triggers {0}", "달성 횟수 {0}");
        Lang.Add("累计打卡 {0} 次", "Checked in {0} times", "출석 {0}회");
        // 逐行翻之后是「按行」查表的，所以整条带 \n 的键用不上，得按行收。
        Lang.Add("Coding Coding bug何尝不是ccb呢", "Coding Coding bug — isn't that ccb too?",
                "Coding Coding bug, 그것도 ccb 아닐까");
        Lang.Add("致敬传奇找bug人自语", "A tribute to the legendary bug hunter's monologue",
                "전설의 버그 헌터의 혼잣말");
        // 「检查并清理异常通关记录」对话框的八行说明：整块拼完再赋值就一句都不翻，必须逐行收。
        Lang.Add("  · 开过自动播放（Autoplay）\n", "  · Autoplay was on\n", "  · 자동 재생을 켠 적 있음\n");
        Lang.Add("  · 开过不会失败（No Fail）\n", "  · No Fail was on\n", "  · 실패 없음을 켠 적 있음\n");
        Lang.Add("  · 用了检查点（旧规则留下的）\n", "  · Checkpoints were used (left over from the old rule)\n",
                "  · 체크포인트를 사용함 (옛 규칙의 흔적)\n");
        Lang.Add("  · 准确度为 0 的空壳数据\n\n", "  · Empty shells with 0 accuracy\n\n", "  · 정확도가 0인 빈 껍데기 데이터\n\n");
        Lang.Add("空敲（没有砖块时按下的那一下，记在「太早/很早」里）不算异常，一律保留。\n",
                "Extra presses (hitting with no tile there, counted under Too Early/Very Early) are not abnormal and are always kept.\n",
                "헛입력(타일이 없을 때 누른 것, 「너무 이름/매우 이름」에 기록)은 이상이 아니므로 그대로 유지합니다.\n");
        Lang.Add("判定数比谱面少一点点也只是放行 —— 只有少得离谱的才会单独列出来供你自己判断。\n",
                "Slightly fewer hits than tiles is also fine — only wildly off ones get listed for you to judge.\n",
                "판정 수가 타일보다 조금 적은 것도 그냥 통과시킵니다 — 크게 벗어난 것만 따로 보여드립니다.\n");
        Lang.Add("删除前会自动把 records.txt 备份一份，可以回滚。", "records.txt is backed up before deleting, so it can be rolled back.",
                "삭제 전에 records.txt를 자동으로 백업하므로 되돌릴 수 있습니다.");
        Lang.Add("扫到 {0} 条建议清理；另有 {1} 条判定数偏少（保留观察，不删）",
                "Found {0} records worth cleaning; {1} more have low hit counts (kept for now, not deleted)",
                "정리할 기록 {0}건 발견; 판정 수가 적은 {1}건은 일단 보류 (삭제하지 않음)");
        Lang.Add("没有需要清理的记录（另有 {0} 条判定数偏少，保留观察）",
                "Nothing to clean ({0} records have low hit counts and are kept for now)",
                "정리할 기록이 없습니다 (판정 수가 적은 {0}건은 일단 보류)");
    }

    /// <summary>顶部导航排（五块小方块）与主界面大厅：标签、统计条、公告横幅。</summary>
    public static void Batch9() {
        // ---------- 导航排（两字标签） ----------
        Lang.Add("官谱", "Official", "공식");
        Lang.Add("谱面", "Charts", "채보");
        Lang.Add("开发", "Dev", "개발");
        // ---------- 大厅：大按钮与统计条 ----------
        Lang.Add("谱面库", "Chart Library", "채보 라이브러리");
        Lang.Add("时长", "Play time", "플레이 시간");
        Lang.Add("官方谱面", "Official charts", "공식 채보");
        Lang.Add("本地谱面", "Local charts", "로컬 채보");
        Lang.Add("已解锁", "Unlocked", "해금");
        Lang.Add("成就等级 {0}", "Level {0}", "업적 레벨 {0}");
        Lang.Add("经验 {0} / {1}", "EXP {0} / {1}", "경험치 {0} / {1}");
        // ---------- 大厅：公告横幅与全文覆盖层 ----------
        Lang.Add("查看全部", "View all", "전체 보기");
    }
    /// <summary>TUF 社区定级（批量评级窗口：进度页 / 三栏 / 候选确认 / 来源标注）。</summary>
    public static void Batch10() {
        // ---------- 入口与按钮 ----------
        Lang.Add("查看进度", "View progress", "진행 보기");
        Lang.Add("继续评级", "Resume rating", "평가 계속");
        Lang.Add("中断（进度留着，可继续）", "Stop (progress kept, can resume)", "중단 (진행 유지, 이어서 가능)");
        Lang.Add("重新扫一遍（清空查询记录）", "Rescan (clear query log)", "다시 검사 (조회 기록 삭제)");
        Lang.Add("关闭窗口", "Close window", "창 닫기");
        Lang.Add("跳过", "Skip", "건너뛰기");
        Lang.Add("这条用本地估算兜底", "Use local estimate for this one", "이건 로컬 추정으로 대체");
        // ---------- 进度页 ----------
        Lang.Add("TUF 社区定级 · 批量评级", "TUF Community Ratings · Batch", "TUF 커뮤니티 등급 · 일괄 평가");
        Lang.Add("准备中…", "Preparing…", "준비 중…");
        Lang.Add("正在查 TUF：{0} / {1}", "Querying TUF: {0} / {1}", "TUF 조회 중: {0} / {1}");
        Lang.Add("正在扫描谱面库…", "Scanning chart library…", "채보 라이브러리 검사 중…");
        Lang.Add("批量评级完成（进度已存，可接着确认）", "Batch rating finished (progress saved, you can keep confirming)",
                 "일괄 평가 완료 (진행 저장됨, 계속 확인 가능)");
        Lang.Add("自动写入 {0} · 待确认 {1} · 查不到 {2}", "Auto {0} · To confirm {1} · Not found {2}",
                 "자동 {0} · 확인 필요 {1} · 못 찾음 {2}");
        Lang.Add("跳过你已标记的 {0} 张 ｜ 本轮已处理 {1} 条", "Skipped {0} you already marked ｜ processed {1} this run",
                 "이미 표시한 {0}장 건너뜀 ｜ 이번에 {1}개 처리");
        Lang.Add("扫到 {0} 张谱面", "Found {0} charts", "채보 {0}장 발견");
        Lang.Add("写盘前已备份", "Backed up before writing", "저장 전 백업 완료");
        Lang.Add("谱师回落命中 {0} 张 ｜ 本地谱师为空没走这条路 {1} 张",
                 "Charter fallback matched {0} ｜ skipped {1} (no local charter name)",
                 "제작자 대체 일치 {0}장 ｜ 로컬 제작자 없음 {1}장");
        // ---------- 自动流程（批量导入 → 检验重复 → 评级 的第三段）----------
        Lang.Add("正在评级：{0} / {1}", "Rating: {0} / {1}", "평가 중: {0} / {1}");
        Lang.Add("TUF 定级 {0} · 本地估算 {1} · 没评上 {2}", "TUF {0} · local estimate {1} · unrated {2}",
                 "TUF {0} · 로컬 추정 {1} · 미평가 {2}");
        Lang.Add("评级完了：TUF 定级 {0} 张 · 本地估算 {1} 张 · 没评上 {2} 张",
                 "Rating done: TUF {0} · local estimate {1} · unrated {2}",
                 "평가 완료: TUF {0}장 · 로컬 추정 {1}장 · 미평가 {2}장");
        // ---------- 三栏标题与说明 ----------
        Lang.Add("自动写入", "Auto-written", "자동 기록");
        Lang.Add("自动写入 {0}", "Auto-written {0}", "자동 기록 {0}");
        Lang.Add("需要你确认", "Needs your confirmation", "확인 필요");
        Lang.Add("需要你确认 {0}", "Needs confirmation {0}", "확인 필요 {0}");
        Lang.Add("查不到", "Not found", "못 찾음");
        Lang.Add("查不到 {0}", "Not found {0}", "못 찾음 {0}");
        Lang.Add("TUF 定级可靠，已经写进 marks.txt（来源：社区定级）",
                 "Reliable TUF rating, already written to marks.txt (source: community)",
                 "TUF 등급 신뢰 가능, marks.txt에 기록됨 (출처: 커뮤니티)");
        Lang.Add("多候选 / 艺术家或曲名对不上 / 特殊档 —— 点一条候选就写入",
                 "Multiple candidates / artist or title mismatch / special tier — click one candidate to write",
                 "후보 여러 개 / 아티스트나 곡명 불일치 / 특수 등급 — 후보 하나를 클릭하면 기록");
        Lang.Add("TUF 没有可用定级 —— 已按本地 AutoRating 兜底（来源：本地估算）",
                 "No usable TUF rating — fell back to local AutoRating (source: local estimate)",
                 "사용 가능한 TUF 등급 없음 — 로컬 AutoRating으로 대체 (출처: 로컬 추정)");
        Lang.Add("这一轮没有能自动写入的谱面", "No chart could be auto-written this run",
                 "이번에는 자동 기록할 채보가 없습니다");
        Lang.Add("这一轮没有查不到的谱面", "No chart was missing this run", "이번에는 못 찾은 채보가 없습니다");
        // ---------- 候选确认 ----------
        Lang.Add("没有需要你确认的谱面喵", "Nothing needs your confirmation meow", "확인할 채보가 없어요냥");
        Lang.Add("TUF 有多种可能，请你认一下", "TUF has several possibilities — please pick one",
                 "TUF에 여러 가능성이 있어요 — 하나 골라주세요");
        Lang.Add("TUF 已经看到 {0} 条候选（点哪一条就按哪一条写）",
                 "TUF shows {0} candidates (clicking one writes that one)",
                 "TUF 후보 {0}개 (클릭한 것으로 기록됩니다)");
        Lang.Add("第 {0} / {1} 条：{2}", "Item {0} / {1}: {2}", "항목 {0} / {1}: {2}");
        Lang.Add("……还有 {0} 条候选没列出来", "…{0} more candidates not listed", "…나열되지 않은 후보 {0}개 더");
        Lang.Add("（特殊档·按规矩不写）", "(special tier · not written by rule)", "(특수 등급 · 규칙상 기록 안 함)");
        Lang.Add("这条先留着，下次点「继续评级」还能看见喵",
                 "Keeping this one — you will see it again when you click Resume rating meow",
                 "이건 남겨둘게요 — '평가 계속'을 누르면 다시 보여요냥");
        Lang.Add("已中断，进度留着喵（下次点「继续评级」接着来）",
                 "Stopped — progress kept meow (click Resume rating next time)",
                 "중단됐어요 — 진행은 저장됨냥 (다음에 '평가 계속'을 누르세요)");
        Lang.Add("这一轮还在跑喵", "This run is still going meow", "이번 작업이 아직 돌고 있어요냥");
        // ---------- 列表里的固定段 ----------
        Lang.Add("未评级", "Unrated", "미평가");
        Lang.Add("社区定级", "Community rating", "커뮤니티 등급");
        Lang.Add("本地估算", "Local estimate", "로컬 추정");
        Lang.Add("……还有 {0} 条（名单存在 mod\\tuf_batch.json）",
                 "…{0} more (full list in mod\\tuf_batch.json)",
                 "…{0}개 더 (전체 목록은 mod\\tuf_batch.json)");
        // ---------- 三栏里那两栏的"为什么落这儿" ----------
        Lang.Add("一致性告警（{0}）：需要你看一眼再定",
                 "Consistency warning ({0}): please take a look", "일관성 경고 ({0}): 한번 봐주세요");
        // ---------- 大厅右下的信息块（反馈与投稿渠道）----------
        Lang.Add("信息", "Info", "정보");
        Lang.Add("1. BUG 反馈渠道 QQ：1758053687", "1. Bug reports — QQ: 1758053687",
                 "1. 버그 제보 — QQ: 1758053687");
        Lang.Add("2. 交流群（QQ）：959648017", "2. Community group (QQ): 959648017",
                 "2. 커뮤니티 그룹 (QQ): 959648017");
        Lang.Add("3. 成就投稿或功能需求也可以走上面两条路线",
                 "3. Achievement submissions or feature requests — use the two channels above",
                 "3. 업적 제보나 기능 요청도 위 두 경로로 보내주세요");
        Lang.Add("4. 寻找合作者ing", "4. Looking for collaborators", "4. 협업자 구하는 중");
    }
}
