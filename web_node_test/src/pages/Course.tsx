import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAppContext } from '../context/AppContext';
import { allLessonIds, courseModules } from '../data/courseData';
import './Course.css';

type InstructionSection = {
  order: number;
  title: string;
  summary: string;
  file: string;
  basePath: string;
};

const instructionSections: InstructionSection[] = [
  {
    order: 2,
    title: 'Установка и запуск плагина Node2Code',
    summary: 'Импорт UnityPackage, подключение зависимостей и первый запуск окна Node2Code.',
    file: '/Instructions/Section_1/Installation.md',
    basePath: '/Instructions/Section_1/',
  },
  {
    order: 3,
    title: 'Обзор интерфейса и навигация',
    summary: 'Функциональные зоны редактора, вкладки холстов и переходы между уровнями проекта.',
    file: '/Instructions/Section_2/Interface.md',
    basePath: '/Instructions/Section_2/',
  },
  {
    order: 4,
    title: 'Глоссарий и основные понятия',
    summary: 'Графы, ноды, порты, потоки выполнения, типы данных и устройство логических блоков.',
    file: '/Instructions/Section_3/Glossary.md',
    basePath: '/Instructions/Section_3/',
  },
  {
    order: 5,
    title: 'Классы, поля и методы',
    summary: 'Проектирование архитектуры программы на холсте классов и настройка членов класса.',
    file: '/Instructions/Section_4/Class.md',
    basePath: '/Instructions/Section_4/',
  },
  {
    order: 6,
    title: 'Логика на холсте метода',
    summary: 'Параметры метода, тело графа, подпространства, точка входа и возврат значений.',
    file: '/Instructions/Section_5/Method.md',
    basePath: '/Instructions/Section_5/',
  },
  {
    order: 7,
    title: 'Справочник нод',
    summary: 'Математика, логика, сравнение, управление потоком, циклы, условия и преобразования.',
    file: '/Instructions/Section_6/Nodes.md',
    basePath: '/Instructions/Section_6/',
  },
  {
    order: 8,
    title: 'Пошаговый практический пример',
    summary: 'Создание класса Calculator и метода Factorial с циклом, условием и возвратом результата.',
    file: '/Instructions/Section_7/Example.md',
    basePath: '/Instructions/Section_7/',
  },
];

const renderInline = (text: string) => {
  const parts = text.split(/(`[^`]+`|\*\*[^*]+\*\*|\$[^$]+\$)/g);

  return parts.map((part, index) => {
    if (part.startsWith('`') && part.endsWith('`')) {
      return <code key={`${part}-${index}`}>{part.slice(1, -1)}</code>;
    }

    if (part.startsWith('**') && part.endsWith('**')) {
      return <strong key={`${part}-${index}`}>{part.slice(2, -2)}</strong>;
    }

    if (part.startsWith('$') && part.endsWith('$')) {
      return <span key={`${part}-${index}`}>{part.slice(1, -1)}</span>;
    }

    return <React.Fragment key={`${part}-${index}`}>{part}</React.Fragment>;
  });
};

const resolveAssetPath = (basePath: string, src: string) => {
  if (/^(https?:|\/)/.test(src)) {
    return src;
  }

  return `${basePath}${src}`;
};

const renderMarkdown = (markdown: string, basePath: string) => {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const elements: React.ReactNode[] = [];
  let index = 0;

  const collectUntil = (predicate: (line: string) => boolean) => {
    const collected: string[] = [];

    while (index < lines.length && !predicate(lines[index])) {
      if (lines[index].trim()) {
        collected.push(lines[index].trim());
      }
      index += 1;
    }

    return collected;
  };

  while (index < lines.length) {
    const line = lines[index];
    const trimmed = line.trim();

    if (!trimmed) {
      index += 1;
      continue;
    }

    if (trimmed === '---') {
      elements.push(<hr key={`hr-${index}`} />);
      index += 1;
      continue;
    }

    if (trimmed.startsWith('```') || trimmed.startsWith('~~~')) {
      const fence = trimmed.slice(0, 3);
      const codeLines: string[] = [];
      index += 1;

      while (index < lines.length && !lines[index].trim().startsWith(fence)) {
        codeLines.push(lines[index]);
        index += 1;
      }

      index += 1;
      elements.push(<pre key={`code-${index}`}><code>{codeLines.join('\n')}</code></pre>);
      continue;
    }

    const imageMatch = trimmed.match(/^!\[(.*)]\((.*)\)$/);
    if (imageMatch) {
      elements.push(
        <figure key={`image-${index}`}>
          <img src={resolveAssetPath(basePath, imageMatch[2])} alt={imageMatch[1] || 'Скриншот инструкции'} />
          {imageMatch[1] ? <figcaption>{imageMatch[1]}</figcaption> : null}
        </figure>,
      );
      index += 1;
      continue;
    }

    const headingMatch = trimmed.match(/^(#{1,4})\s+(.*)$/);
    if (headingMatch) {
      const level = Math.min(headingMatch[1].length, 4);
      const content = renderInline(headingMatch[2]);

      if (level === 1) {
        elements.push(<h1 key={`heading-${index}`}>{content}</h1>);
      } else if (level === 2) {
        elements.push(<h2 key={`heading-${index}`}>{content}</h2>);
      } else if (level === 3) {
        elements.push(<h3 key={`heading-${index}`}>{content}</h3>);
      } else {
        elements.push(<h4 key={`heading-${index}`}>{content}</h4>);
      }

      index += 1;
      continue;
    }

    if (trimmed.startsWith('>')) {
      const quote = collectUntil((currentLine) => !currentLine.trim().startsWith('>'))
        .map((quoteLine) => quoteLine.replace(/^>\s?/, ''))
        .join(' ');
      elements.push(<blockquote key={`quote-${index}`}>{renderInline(quote)}</blockquote>);
      continue;
    }

    if (/^\|.+\|$/.test(trimmed)) {
      const tableRows = collectUntil((currentLine) => !/^\|.+\|$/.test(currentLine.trim()));
      const normalizedRows = tableRows
        .filter((row) => !/^\|\s*-+/.test(row))
        .map((row) => row.split('|').slice(1, -1).map((cell) => cell.trim()));
      const [head, ...body] = normalizedRows;

      elements.push(
        <div className="instruction-table-wrap" key={`table-${index}`}>
          <table>
            {head ? (
              <thead>
                <tr>{head.map((cell) => <th key={cell}>{renderInline(cell)}</th>)}</tr>
              </thead>
            ) : null}
            <tbody>
              {body.map((row, rowIndex) => (
                <tr key={`row-${rowIndex}`}>
                  {row.map((cell, cellIndex) => <td key={`${cell}-${cellIndex}`}>{renderInline(cell)}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>,
      );
      continue;
    }

    if (/^[-*]\s+/.test(trimmed)) {
      const items = collectUntil((currentLine) => !/^[-*]\s+/.test(currentLine.trim()));
      elements.push(
        <ul key={`ul-${index}`}>
          {items.map((item, itemIndex) => (
            <li key={`${item}-${itemIndex}`}>{renderInline(item.replace(/^[-*]\s+/, ''))}</li>
          ))}
        </ul>,
      );
      continue;
    }

    if (/^\d+\.\s+/.test(trimmed)) {
      const items = collectUntil((currentLine) => !/^\d+\.\s+/.test(currentLine.trim()));
      elements.push(
        <ol key={`ol-${index}`}>
          {items.map((item, itemIndex) => (
            <li key={`${item}-${itemIndex}`}>{renderInline(item.replace(/^\d+\.\s+/, ''))}</li>
          ))}
        </ol>,
      );
      continue;
    }

    const paragraph = collectUntil((currentLine) => {
      const current = currentLine.trim();
      return !current
        || current === '---'
        || current.startsWith('#')
        || current.startsWith('>')
        || current.startsWith('```')
        || current.startsWith('~~~')
        || current.startsWith('![')
        || /^[-*]\s+/.test(current)
        || /^\d+\.\s+/.test(current)
        || /^\|.+\|$/.test(current);
    }).join(' ');

    elements.push(<p key={`p-${index}`}>{renderInline(paragraph)}</p>);
  }

  return elements;
};

const Course: React.FC = () => {
  const { completedLessons } = useAppContext();
  const [activeSection, setActiveSection] = useState<InstructionSection | null>(null);
  const [activeMarkdown, setActiveMarkdown] = useState('');
  const [isLoadingSection, setIsLoadingSection] = useState(false);
  const [sectionError, setSectionError] = useState('');
  const totalLessons = allLessonIds.length;
  const overallProgress = Math.round((completedLessons.length / totalLessons) * 100);

  useEffect(() => {
    if (!activeSection) {
      setActiveMarkdown('');
      setSectionError('');
      return;
    }

    const controller = new AbortController();

    setIsLoadingSection(true);
    setSectionError('');

    fetch(activeSection.file, { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error('Не удалось загрузить текст раздела');
        }

        return response.text();
      })
      .then(setActiveMarkdown)
      .catch((error: Error) => {
        if (error.name !== 'AbortError') {
          setSectionError(error.message);
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoadingSection(false);
        }
      });

    return () => controller.abort();
  }, [activeSection]);

  return (
    <section className="course-page">
      <div className="course-hero">
        <div>
          <span className="course-eyebrow">Раздел инструкции</span>
          <h1>Инструкция по старту работы с Node2Code</h1>
        </div>

        <div className="course-overview-card">
          <strong>{overallProgress}% пройдено</strong>
          <span>{completedLessons.length} из {totalLessons} уроков завершено</span>
          <div className="progress-line">
            <div style={{ width: `${overallProgress}%` }} />
          </div>
        </div>
      </div>

      <div className="course-summary-grid">
        <article className="course-summary-card">
          <h2>Что внутри</h2>
          <p>Unity Hub, установка редактора и базовые окна Unity для уверенного старта.</p>
        </article>
        <article className="course-summary-card">
          <h2>Как устроен раздел</h2>
          <p>Первый модуль ведет по урокам, а следующие разделы открываются как подробные инструкции с текстом и скриншотами.</p>
        </article>
        <article className="course-summary-card">
          <h2>Результат</h2>
          <p>Пользователь получает готовую стартовую среду и понимает, как открыть плагин и перейти к дальнейшей работе.</p>
        </article>
      </div>

      <div className="topic-grid">
        {courseModules.map((module) => {
          const completedCount = module.lessons.filter((lesson) => completedLessons.includes(lesson.id)).length;
          const progress = Math.round((completedCount / module.lessons.length) * 100);

          return (
            <article key={module.id} className="topic-card">
              <div className="topic-card__top">
                <span className="topic-order">Модуль {module.order}</span>
                <span className="topic-meta">{module.lessons.length} урока</span>
              </div>

              <h3>{module.title}</h3>

              <ul className="topic-preview-list">
                {module.lessons.slice(0, 3).map((lesson) => (
                  <li key={lesson.id}>{lesson.title}</li>
                ))}
              </ul>

              <div className="progress-wrap">
                <div className="progress-line">
                  <div style={{ width: `${progress}%` }} />
                </div>
                <span>{progress}%</span>
              </div>

              <Link to={`/topic/${module.id}`} className={`topic-btn ${progress > 0 ? 'continue' : 'start'}`}>
                {progress > 0 ? 'Продолжить' : 'Начать'}
              </Link>
            </article>
          );
        })}

        {instructionSections.map((section) => (
          <article key={section.order} className="topic-card">
            <div className="topic-card__top">
              <span className="topic-order">Модуль {section.order}</span>
              <span className="topic-meta">Раздел</span>
            </div>

            <h3>{section.title}</h3>

            <ul className="topic-preview-list">
              <li>{section.summary}</li>
            </ul>

            <button type="button" className="topic-btn instruction-open" onClick={() => setActiveSection(section)}>
              Открыть
            </button>
          </article>
        ))}
      </div>

      {activeSection ? (
        <div className="instruction-modal" role="dialog" aria-modal="true" aria-labelledby="instruction-modal-title">
          <button type="button" className="instruction-modal__backdrop" aria-label="Закрыть раздел" onClick={() => setActiveSection(null)} />
          <div className="instruction-modal__panel">
            <div className="instruction-modal__head">
              <div>
                <span className="topic-order">Модуль {activeSection.order}</span>
                <h2 id="instruction-modal-title">{activeSection.title}</h2>
              </div>
              <button type="button" className="instruction-modal__close" onClick={() => setActiveSection(null)}>
                Закрыть
              </button>
            </div>

            <div className="instruction-content">
              {isLoadingSection ? <p>Загружаем раздел...</p> : null}
              {sectionError ? <p>{sectionError}</p> : null}
              {!isLoadingSection && !sectionError ? renderMarkdown(activeMarkdown, activeSection.basePath) : null}
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
};

export default Course;
