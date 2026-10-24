namespace LevelLibrary;

/// <summary>多语言词条表 · 第二批：成就的名字与触发条件（与 `Lang.Data.cs` 同一套注册表）。
/// 只收固定文本；动态内容（谱面名 / 玩家名 / 日期 / 数字）故意不收。
/// 中文原文必须与源码里逐字一致（标点、空格、全角半角都算），差一个字 `Lang.Tr` 就查不到。
/// 词条靠 `LangData` 静态构造器里的显式调用注册，本类自己的静态构造器不会有人碰。</summary>
public static class LangData2 {
    /// <summary>注册成就的名字与触发条件（按清单顺序）</summary>
    public static void Batch3() {
        Lang.Add("打开主界面", "Open the Main Screen", "메인 화면 열기");
        Lang.Add("第一次打开主界面", "Open the main screen for the first time", "메인 화면을 처음 열기");
        Lang.Add("打开新世界的大门？", "Opening the door to a new world?", "새로운 세계의 문을 여는 걸까?");
        Lang.Add("批量导入谱面", "Batch Import Charts", "채보 일괄 가져오기");
        Lang.Add("第一次使用批量导入谱面功能", "Use batch import for the first time", "채보 일괄 가져오기를 처음 사용");
        Lang.Add("谱间宇宙", "A Universe of Charts", "채보 사이의 우주");
        Lang.Add("小牢", "Small Grind", "작은 노가다");
        Lang.Add("单个谱面游玩次数达到 500 次", "Play one chart 500 times", "한 채보를 500회 플레이");
        Lang.Add("我一定能击破这个谱子", "I will definitely beat this chart", "이 채보는 반드시 격파할 거야");
        Lang.Add("中牢", "Medium Grind", "중간 노가다");
        Lang.Add("单个谱面游玩次数达到 1000 次", "Play one chart 1000 times", "한 채보를 1000회 플레이");
        Lang.Add("你到底有多喜欢这个谱？", "Just how much do you like this chart?", "도대체 이 채보를 얼마나 좋아하는 거야?");
        Lang.Add("牢大", "Grind Lord", "노가다의 왕");
        Lang.Add("单个谱面游玩次数达到 10000 次", "Play one chart 10000 times", "한 채보를 10000회 플레이");
        Lang.Add("是什么支持你这么做的？时间吗？", "What kept you going? Time?", "무엇이 너를 여기까지 밀어줬어? 시간?");
        Lang.Add("【掌中宇宙】", "[Universe in the Palm]", "[손바닥 속의 우주]");
        Lang.Add("首次打开关卡编辑器打开谱面", "Open a chart in the level editor for the first time", "레벨 에디터에서 채보를 처음 열기");
        Lang.Add("吾掌中，寰宇也", "The universe rests in my palm", "내 손바닥 안에 온 세상이 있다");
        Lang.Add("月正向山倾", "The Moon Tilts Toward the Mountain", "달이 산으로 기울다");
        Lang.Add("搜索月正向山倾", "Search for the Moon-Tilting Mountain", "달이 산으로 기울다를 검색");
        Lang.Add("咯咯哒", "Cluck Cluck", "꼬끼오");
        Lang.Add("点击开发者页的「母鸡先生」6 次", "Click Mr. Hen on the developer page 6 times", "개발자 페이지의 미스터 헨을 6번 클릭");
        Lang.Add("咯咯哒咯咯哒咯咯哒", "Cluck cluck cluck cluck cluck", "꼬끼오 꼬끼오 꼬끼오");
        Lang.Add("在开发者界面输入 bugbug", "Type bugbug on the developer screen", "개발자 화면에서 bugbug 입력");
        Lang.Add("玩家名字为 Ziyu3 的人点击登录进入界面时触发", "Triggered when a player named Ziyu3 logs in", "이름이 Ziyu3인 플레이어가 로그인하면 달성");
        Lang.Add("你能寻找到任何漏洞，你是BEST hunter。", "You can find any bug — you are the BEST hunter.",
                "너는 어떤 버그든 찾아낼 수 있어, 너는 BEST hunter.");
        Lang.Add("连续用谱面库打开 1000 次谱面（中途用游戏自己的方式打开会重新计数）",
                "Open charts from the library 1000 times in a row (opening one the game's own way resets it)",
                "채보 라이브러리로 1000회 연속 열기 (게임 자체 방식으로 열면 처음부터 다시)");
        Lang.Add("我说了，我会让LevelLibrary越来越好，很感谢你的支持。",
                "I said I would keep making LevelLibrary better. Thank you for your support.",
                "말했잖아, LevelLibrary를 계속 더 좋게 만들 거라고. 응원해줘서 고마워.");
        Lang.Add("兴酣落笔摇五岳", "Inspired, the Brush Shakes the Five Peaks", "흥이 오르면 붓이 오악을 흔든다");
        Lang.Add("一个谱面游玩次数为1时通关", "Clear a chart on its very first play", "채보를 첫 플레이에 클리어");
        Lang.Add("新手村的大门", "The Gate of the Starter Village", "초보 마을의 문");
        Lang.Add("标判通过一个标记P17的谱面", "Clear a P17 chart with normal judging", "일반 판정으로 P17 채보 클리어");
        Lang.Add("恭喜你，走进新手村的大门啦", "Congrats, you have entered the starter village", "축하해, 초보 마을에 들어왔어");
        Lang.Add("新手村……未来的开始", "Starter Village... Where the Future Begins", "초보 마을... 미래의 시작");
        Lang.Add("严判标记P17的谱面", "Clear a P17 chart with strict judging", "엄격 판정으로 P17 채보 클리어");
        Lang.Add("历经万难，终成道果，你还会继续走多远呢？", "Through every hardship you reached the fruit of the path — how much further will you go?",
                "온갖 고난을 지나 마침내 결실을 맺었어. 앞으로 얼마나 더 나아갈 거야?");
        Lang.Add("宽判或标判或严判通过 G1-20 的谱面", "Clear a G1-20 chart with any judging", "아무 판정으로 G1-20 채보 클리어");
        Lang.Add("严判通关 P10 及以上的谱面", "Clear a P10 or higher chart with strict judging", "엄격 판정으로 P10 이상 채보 클리어");
        Lang.Add("你已经学会了点击键盘，现在去击破2026吧", "You have learned to hit the keys — now go beat 2026",
                "키보드 누르는 법은 배웠어, 이제 2026을 격파하러 가자");
        Lang.Add("总游玩次数达到 1000", "Reach 1000 total plays", "누적 플레이 1000회 달성");
        Lang.Add("一千次了…………打什么谱子要用这么多次呢", "A thousand times... what chart needs that many tries?",
                "벌써 천 번이라니... 무슨 채보가 이렇게 많이 필요해?");
        Lang.Add("累计登录天数达到 100 天", "Reach 100 total login days", "누적 로그인 100일 달성");
        Lang.Add("100天了诶，你真的很喜欢这个游戏", "100 days already — you really love this game",
                "벌써 100일이야, 너는 정말 이 게임을 좋아하는구나");
        Lang.Add("地球年", "Earth Year", "지구년");
        Lang.Add("累计登录天数达到 365 天", "Reach 365 total login days", "누적 로그인 365일 달성");
        Lang.Add("年年岁岁，岁岁年年", "Year after year, and year after year", "해가 가고 또 해가 오네");
        Lang.Add("累计游玩时间达到 100 小时", "Reach 100 hours of total play time", "누적 플레이 100시간 달성");
        Lang.Add("玩了100小时啦，还会有多少个100H呢？", "100 hours in — how many more 100-hour marks will there be?",
                "벌써 100시간이야, 앞으로 몇 번의 100시간이 더 있을까?");
        Lang.Add("【万】", "[Ten Thousand]", "[만]");
        Lang.Add("总游玩次数达到 10000", "Reach 10000 total plays", "누적 플레이 10000회 달성");
        Lang.Add("你真的很热爱这个游戏", "You truly love this game", "너는 정말 이 게임을 사랑하는구나");
        Lang.Add("【持之以恒】", "[Perseverance]", "[끈기]");
        Lang.Add("总游玩次数达到 100000", "Reach 100000 total plays", "누적 플레이 100000회 달성");
        Lang.Add("十万……许多人不可企及的数字，你做到了", "A hundred thousand... a number many never reach, and you did it",
                "십만... 많은 이가 닿지 못하는 숫자야, 너는 해냈어");
        Lang.Add("【列表空间】", "[List Space]", "[목록 공간]");
        Lang.Add("列表谱面数达到 20 个", "Have 20 charts in your library", "목록에 채보 20개");
        Lang.Add("【列表矩阵】", "[List Matrix]", "[목록 행렬]");
        Lang.Add("列表谱面数达到 200 个", "Have 200 charts in your library", "목록에 채보 200개");
        Lang.Add("【谱面宇宙】", "[Chart Universe]", "[채보 우주]");
        Lang.Add("列表谱面数达到 2000 个", "Have 2000 charts in your library", "목록에 채보 2000개");
        Lang.Add("全即一？", "All is one?", "전부가 하나?");
        Lang.Add("首次通关谱面", "First Clear", "첫 클리어");
        Lang.Add("宽判/标判/严判通关任意谱面", "Clear any chart with any judging", "아무 판정으로 아무 채보나 클리어");
        Lang.Add("你迈出了第一步", "You took the first step", "너는 첫 걸음을 내디뎠어");
        Lang.Add("打谱新手", "Chart Rookie", "채보 초보");
        Lang.Add("通关 10 个不同谱面", "Clear 10 different charts", "서로 다른 채보 10개 클리어");
        Lang.Add("这10个谱子里都是你喜欢的吗", "Are all ten of them your favorites?", "이 10개 채보가 다 마음에 들어?");
        Lang.Add("打谱高手", "Chart Expert", "채보 고수");
        Lang.Add("通关 100 个不同谱子", "Clear 100 different charts", "서로 다른 채보 100개 클리어");
        Lang.Add("100个……你一定练了很久吧……", "A hundred... you must have practiced a long time...",
                "100개... 분명 오래 연습했겠지...");
        Lang.Add("龙币", "Dragon Coin", "드래곤 코인");
        Lang.Add("通关 1000 个不同谱子", "Clear 1000 different charts", "서로 다른 채보 1000개 클리어");
        Lang.Add("1000个？！大佬教教我…………", "A thousand?! Teach me, master...", "1000개?! 고수님 알려주세요...");
        Lang.Add("宽判或标判或严判通过 U1-20 的谱面", "Clear a U1-20 chart with any judging", "아무 판정으로 U1-20 채보 클리어");
        Lang.Add("坚持", "Keep Going", "버티기");
        Lang.Add("单个谱面游玩次数超过 100 后仍未通关", "Play one chart over 100 times without clearing it",
                "한 채보를 100회 넘게 플레이해도 클리어 못 함");
        Lang.Add("再坚持一下，说不定就过了呢？", "Hold on a little longer — maybe this is the run?",
                "조금만 더 버텨봐, 어쩌면 이번에 될지도 몰라?");
        Lang.Add("耐心", "Patience", "인내");
        Lang.Add("单个谱面游玩次数超过 500 后仍未通关", "Play one chart over 500 times without clearing it",
                "한 채보를 500회 넘게 플레이해도 클리어 못 함");
        Lang.Add("可能下一把就过了？", "Maybe the next try is the one?", "다음 판이면 될지도?");
        Lang.Add("要不……我们休息一会？", "How about... we take a break?", "그럼... 우리 잠깐 쉬는 게 어때?");
        Lang.Add("单个谱面游玩次数超过 1000 后仍未通关", "Play one chart over 1000 times without clearing it",
                "한 채보를 1000회 넘게 플레이해도 클리어 못 함");
        Lang.Add("你真的很累了，休息一会吧", "You really are tired — take a rest", "너는 정말 지쳤어, 좀 쉬어");
        Lang.Add("【炼成坚持蛊】", "[Forged the Gu of Perseverance]", "[인내의 구를 연성]");
        Lang.Add("单个谱面游玩次数超过 2000 后仍未通关", "Play one chart over 2000 times without clearing it",
                "한 채보를 2000회 넘게 플레이해도 클리어 못 함");
        Lang.Add("逆流河上万仙退，失败不敌坚持泪……", "Ten thousand immortals retreat at the upstream river — defeat cannot beat the tears of perseverance...",
                "역류하는 강에서 만 명의 신선이 물러난다, 실패도 인내의 눈물을 이기지 못해...");
        Lang.Add("我的世界，我自己定义", "My world, defined by me", "내 세계는 내가 정의해");
        Lang.Add("第一次自定义主题色", "Pick a custom theme color for the first time",
                "테마 색을 처음으로 직접 고름");
        Lang.Add("个性化的旅程开始了", "The journey of personalization begins", "개성화의 여정이 시작됐어");
        Lang.Add("个性", "Individuality", "개성");
        Lang.Add("头像和名字都设置完毕", "Set both your avatar and your name", "아바타와 이름을 모두 설정");
        Lang.Add("这就是我", "This is me", "이게 나야");
        Lang.Add("世一开", "World First, Open", "세계 1위, 열림");
        Lang.Add("完美无瑕 U15 及以上的谱面", "Pure Perfect a U15 or higher chart", "U15 이상 채보 완벽 무결");
        Lang.Add("开开？！", "Open?!", "열렸어?!");
        Lang.Add("P级全收集", "P Rank Collector", "P 등급 전부 수집");
        Lang.Add("解锁 P1-20 全部里程碑成就", "Unlock every P1-20 milestone achievement", "P1-20 마일스톤 업적 전부 해금");
        Lang.Add("你一定是收集癖吧……", "You must be a collector...", "너는 분명 수집벽이 있구나...");
        Lang.Add("G级全收集", "G Rank Collector", "G 등급 전부 수집");
        Lang.Add("解锁 G1-20 全部里程碑成就", "Unlock every G1-20 milestone achievement", "G1-20 마일스톤 업적 전부 해금");
        Lang.Add("你居然做到了这一步？！", "You actually made it this far?!", "여기까지 해냈다고?!");
        Lang.Add("U级全收集", "U Rank Collector", "U 등급 전부 수집");
        Lang.Add("解锁 U1-20 全部里程碑成就", "Unlock every U1-20 milestone achievement", "U1-20 마일스톤 업적 전부 해금");
        Lang.Add("没关就是开了？", "Not closed means it's on?", "안 껐으면 켠 거지?");
        Lang.Add("打卡！", "Check In!", "출석 체크!");
        Lang.Add("打开关卡编辑器5秒后触发", "Triggered 5 seconds after opening the level editor",
                "레벨 에디터를 열고 5초 후 달성");
        Lang.Add("每日打卡", "Daily Check-in", "매일 출석");
        Lang.Add("今日打谱大学习", "Today's Chart Study", "오늘의 채보 학습");
        Lang.Add("每日随意判定通关第一个谱子时触发", "Triggered by clearing your first chart of the day with any judging",
                "오늘 처음 클리어한 채보에서 달성");
        Lang.Add("每日学习", "Daily Study", "매일 학습");
        Lang.Add("完成谱面！", "Chart Complete!", "채보 완료!");
        Lang.Add("今日打谱大学习触发后，每次从第一格（未用检查点）通关触发",
                "After Today's Chart Study, triggered by clearing from the first tile (no checkpoints)",
                "오늘의 채보 학습 이후, 첫 칸부터(체크포인트 없이) 클리어할 때 달성");
        Lang.Add("今天好！！", "Good Day!!", "오늘도 좋아!!");
        Lang.Add("每日第一次打开谱面库主界面时触发", "Triggered by opening the library main screen for the first time each day",
                "매일 처음 채보 라이브러리 메인 화면을 열 때 달성");
        Lang.Add("今天也要好好的", "Have a good day today too", "오늘도 잘 지내");
        Lang.Add("恭喜你：PP-1", "Congrats: PP-1", "축하해: PP-1");
        Lang.Add("每次通关时除完美外只有一个稍快或稍慢时触发（可重复触发）",
                "Triggered when a clear has exactly one early or late hit besides perfects (repeatable)",
                "클리어 시 완벽 외에 약간 빠름/느림이 하나뿐일 때 달성 (반복 가능)");
        Lang.Add("触发次数：0", "Triggers: 0", "달성 횟수: 0");
        Lang.Add("啊嘞，怎么死在这了", "Huh, how did I die here", "어라, 왜 여기서 죽었지");
        Lang.Add("在完成度 99% 的地方死亡，且这一局必须从第一格开始（没用检查点）时触发（可重复触发）",
                "Triggered by dying at 99% completion with the run started from the first tile (no checkpoints) (repeatable)",
                "진행도 99%에서 죽고, 이 판을 첫 칸부터 시작했을 때 달성 (체크포인트 없음) (반복 가능)");
        // 已删的成就（如「这是什么情况呢？」）三条译文要一起下掉，留一条就是孤儿词条
    }
}
