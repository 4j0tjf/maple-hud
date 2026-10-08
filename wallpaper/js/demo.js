/* API 키가 없을 때 보여주는 예시 데이터 (scheduler/character-state 응답 형식) */
(function (root) {
  'use strict';

  function daily(name, type, now, max, quest, reg) {
    return {
      content_name: name,
      type: type,
      registration_flag: reg === false ? 'false' : 'true',
      now_count: now,
      max_count: max,
      quest_state: quest == null ? null : String(quest)
    };
  }

  function boss(name, difficulty, cycle, order, done) {
    return {
      content_name: name,
      difficulty: difficulty,
      cycle: cycle,
      list_order_no: order,
      registration_flag: 'true',
      complete_flag: done ? 'true' : 'false'
    };
  }

  var characters = [
    {
      name: '단풍잎소녀', world: '스카니아', level: 287, cls: '아크메이지(썬,콜)', exp: '61.204',
      daily: [
        daily('몬스터파크', 'contents', 2, 2),
        daily('우르스', 'contents', 1, 3),
        daily('세르니움 일일 퀘스트', 'quest', 0, 0, 2),
        daily('호텔 아르크스 일일 퀘스트', 'quest', 0, 0, 2),
        daily('오디움 일일 퀘스트', 'quest', 0, 0, 1),
        daily('도원경 일일 퀘스트', 'quest', 0, 0, 0),
        daily('아르테리아 일일 퀘스트', 'quest', 0, 0, 0),
        daily('카르시온 일일 퀘스트', 'quest', 0, 0, 0)
      ],
      weekly: [
        daily('에픽 던전 : 하이마운틴', 'contents', 1, 1),
        daily('무릉도장', 'contents', 0, 1),
        daily('길드 플래그 레이스', 'contents', 0, 1)
      ],
      boss: [
        boss('스우', '하드', '주간', 1, true),
        boss('데미안', '하드', '주간', 2, true),
        boss('가디언 엔젤 슬라임', '카오스', '주간', 3, true),
        boss('루시드', '하드', '주간', 4, true),
        boss('윌', '하드', '주간', 5, false),
        boss('더스크', '카오스', '주간', 6, false),
        boss('진 힐라', '하드', '주간', 7, false),
        boss('듄켈', '하드', '주간', 8, false),
        boss('선택받은 세렌', '하드', '주간', 9, false),
        boss('감시자 칼로스', '노멀', '주간', 10, false),
        boss('검은 마법사', '하드', '월간', 11, false)
      ],
      bossClear: 4, bossLimit: 12
    },
    {
      name: '버섯왕자', world: '스카니아', level: 275, cls: '아델', exp: '12.880',
      daily: [
        daily('몬스터파크', 'contents', 2, 2),
        daily('우르스', 'contents', 3, 3),
        daily('세르니움 일일 퀘스트', 'quest', 0, 0, 2),
        daily('호텔 아르크스 일일 퀘스트', 'quest', 0, 0, 2),
        daily('오디움 일일 퀘스트', 'quest', 0, 0, 2)
      ],
      weekly: [
        daily('에픽 던전 : 하이마운틴', 'contents', 0, 1),
        daily('무릉도장', 'contents', 1, 1)
      ],
      boss: [
        boss('스우', '노멀', '주간', 1, true),
        boss('데미안', '노멀', '주간', 2, true),
        boss('루시드', '노멀', '주간', 3, true),
        boss('윌', '노멀', '주간', 4, true),
        boss('더스크', '노멀', '주간', 5, false),
        boss('진 힐라', '노멀', '주간', 6, false),
        boss('듄켈', '노멀', '주간', 7, false)
      ],
      bossClear: 4, bossLimit: 12
    },
    {
      name: '슬라임대장', world: '스카니아', level: 262, cls: '비숍', exp: '88.017',
      daily: [
        daily('몬스터파크', 'contents', 2, 2),
        daily('세르니움 일일 퀘스트', 'quest', 0, 0, 2),
        daily('호텔 아르크스 일일 퀘스트', 'quest', 0, 0, 2)
      ],
      weekly: [
        daily('무릉도장', 'contents', 1, 1)
      ],
      boss: [
        boss('스우', '노멀', '주간', 1, true),
        boss('데미안', '노멀', '주간', 2, true),
        boss('가디언 엔젤 슬라임', '노멀', '주간', 3, true),
        boss('루시드', '이지', '주간', 4, true)
      ],
      bossClear: 4, bossLimit: 12
    }
  ];

  function build() {
    return characters.map(function (c, i) {
      return {
        name: c.name,
        ocid: 'demo-' + i,
        level: c.level,
        world: c.world,
        cls: c.cls,
        basic: {
          character_name: c.name,
          character_level: c.level,
          character_class: c.cls,
          world_name: c.world,
          character_exp_rate: c.exp,
          character_image: ''
        },
        body: {
          date: null,
          character_name: c.name,
          world_name: c.world,
          character_level: c.level,
          character_class: c.cls,
          daily_contents: c.daily,
          weekly_contents: c.weekly,
          boss_contents: c.boss,
          weekly_boss_clear_count: c.bossClear,
          weekly_boss_clear_limit_count: c.bossLimit
        }
      };
    });
  }

  root.MH = root.MH || {};
  root.MH.demo = { build: build };
})(typeof window !== 'undefined' ? window : globalThis);
