import React from 'react';
import './Plugin.css';

const capabilities = [
  {
    title: 'Ноды в C# и обратно',
    text: 'Плагин двусторонне конвертирует визуальную логику в текстовый код и помогает читать уже написанные скрипты как граф.',
  },
  {
    title: 'Плавный вход в Unity',
    text: 'Редактор нод помогает постепенно собирать игровую логику: от первых переменных и событий до законченных механик.',
  },
  {
    title: 'Расширение через код',
    text: 'Когда стандартных блоков мало, плагин помогает переносить сложную логику в C# и держать ее рядом с визуальным сценарием.',
  },
  {
    title: 'Подходит для прототипирования',
    text: 'Связка графа и кода удобна для быстрых экспериментов, геймдизайна, обучения и высокоуровневой логики игровых событий.',
  },
];

const Plugin: React.FC = () => {
  return (
    <section className="plugin-page">
      <div className="plugin-hero">
        <div className="plugin-hero__content">
          <span className="plugin-badge">Платформа для Unity-плагина</span>
          <h1>Плагин, который связывает визуальное программирование и C# в одном рабочем процессе</h1>
          <p>
            Node2Code помогает собирать игровую логику в виде нод, переводить её в C#
            и, наоборот, разбирать написанный код обратно в понятный визуальный граф.
            Это удобный инструмент и для быстрого прототипирования, и для постепенного перехода
            от наглядной схемы к полноценной разработке на Unity.
          </p>

          <div className="plugin-hero__stats">
            <div>
              <strong>Одна версия для всех ОС</strong>
              <span>единый пакет плагина для быстрого старта на любой платформе</span>
            </div>
            <div>
              <strong>Ноды ↔ C#</strong>
              <span>двусторонняя конвертация логики</span>
            </div>
            <div>
              <strong>Единый пайплайн</strong>
              <span>визуальная схема и код работают как одна система</span>
            </div>
          </div>
        </div>

        <div className="plugin-hero__visual">
          <div className="plugin-panel plugin-panel--graph">
            <div className="plugin-panel__label">Визуальный сценарий</div>
            <div className="plugin-node plugin-node--accent">On Collision Enter</div>
            <div className="plugin-link" />
            <div className="plugin-node">Get Target By Tag</div>
            <div className="plugin-link" />
            <div className="plugin-node plugin-node--success">Change Color</div>
          </div>

          <div className="plugin-panel plugin-panel--code">
            <div className="plugin-panel__label">C# результат</div>
            <pre>{`if (collision.gameObject.CompareTag("Door"))
{
    targetRenderer.material.color = openColor;
}`}</pre>
          </div>
        </div>
      </div>

      <div className="plugin-section-grid">
        <section className="plugin-card plugin-card--install">
          <div className="plugin-card__head">
            <span className="plugin-eyebrow">Установка</span>
            <h2>Установщики</h2>
            <p>
              Скачайте единую версию плагина и установите её в рабочую среду Unity.
              После установки можно сразу открыть визуальный редактор и начать собирать логику проекта.
            </p>
          </div>

          <div className="install-grid">
            <a className="install-tile" href="/node2code-plugin.unitypackage" download="Node2Code_39.unitypackage">
              <strong>Скачать плагин</strong>
              <span>Единая версия пакета для работы на любой поддерживаемой операционной системе</span>
            </a>
          </div>
        </section>

        <section className="plugin-card plugin-card--docs">
          <div className="plugin-card__head">
            <span className="plugin-eyebrow">Документация</span>
            <h2>Материалы по установке и использованию</h2>
            <p>
              Подробная инструкция поможет быстро подключить плагин, разобраться в интерфейсе
              и начать работу с редактором нод без лишних ошибок на старте.
            </p>
          </div>

          <a className="doc-link" href="/node2code-documentation.pdf" download="NODE2CODE-ДОКУМЕНТАЦИЯ.pdf">
            <span className="doc-link__icon">PDF</span>
            <span>
              <strong>Скачать документацию</strong>
              <small>Пошаговая установка, обзор интерфейса и базовые сценарии работы</small>
            </span>
          </a>
        </section>
      </div>

      <section className="plugin-card plugin-card--overview">
        <div className="plugin-card__head">
          <span className="plugin-eyebrow">Описание плагина</span>
          <h2>Что умеет Node2Code</h2>
          <p>
            Node2Code нужен не только для визуального программирования, но и для глубокого
            понимания того, как игровая логика выглядит в коде. Он помогает быстро собирать механику,
            читать связи между блоками, анализировать результат в C# и расширять стандартный набор
            возможностей за счет обычных скриптов и логики проекта.
          </p>
        </div>

        <div className="capabilities-grid">
          {capabilities.map((item) => (
            <article key={item.title} className="capability-card">
              <h3>{item.title}</h3>
              <p>{item.text}</p>
            </article>
          ))}
        </div>

        <div className="plugin-description-layout">
          <div className="plugin-description-block">
            <h3>Как это работает в проекте</h3>
            <p>
              Плагин встраивается прямо в Unity-проект и открывает отдельное окно редактора,
              где можно собирать поведение объектов, связывать данные, работать с событиями,
              столкновениями, движением, созданием объектов и другими игровыми системами.
            </p>
            <ul className="plugin-list">
              <li>Импорт в Unity-проект и запуск редактора нод из рабочей среды.</li>
              <li>Сборка логики объектов, событий, столкновений и игровых механик на графе.</li>
              <li>Генерация C# для анализа, доработки и интеграции с обычным кодом проекта.</li>
              <li>Работа со сложной логикой в C# рядом с визуальным редактором.</li>
            </ul>
          </div>

          <div className="plugin-description-block">
            <h3>Где это особенно полезно</h3>
            <p>
              Node2Code хорошо подходит для быстрого прототипирования, работы с наглядной
              игровой логикой, обучения и ситуаций, где важно быстро увидеть структуру поведения.
              При этом сложные вычисления и нестандартные сценарии можно оставлять в коде,
              сохраняя единый рабочий процесс между визуальной логикой и C#.
            </p>
          </div>
        </div>
      </section>

    </section>
  );
};

export default Plugin;
