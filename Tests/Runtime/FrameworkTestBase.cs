using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace ModernWestern.UEArchitecture.Tests
{
    /// <summary>
    /// Opens the test gate, builds throwaway levels and puts the framework back as it was after each test.
    /// </summary>
    public abstract class FrameworkTestBase
    {
        private readonly List<Object> _created = new();

        private GameObject _templates;

        private Scene _level;

        private Scene _previousActive;

        [SetUp]
        public void OpenGate()
        {
            TestGate.Enabled = true;

            TestGate.Log.Clear();
        }

        [UnityTearDown]
        public IEnumerator RestoreFramework()
        {
            TestGate.Enabled = false;

            if (_level.IsValid() && _level.isLoaded)
            {
                World.End_Internal();

                if (_previousActive.IsValid() && _previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(_previousActive);
                }

                yield return SceneManager.UnloadSceneAsync(_level);
            }

            foreach (var created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();

            Timers.Reset_Internal();

            GameFramework.Relaunch_Internal();
        }

        protected static void Relaunch(UEArchitectureSettings settings = null)
        {
            GameFramework.Relaunch_Internal(settings);
        }

        /// <summary>
        /// A stand-in for a prefab: an active object under an inactive parent, so it never runs but its copies do.
        /// </summary>
        protected T Template<T>(string name) where T : Component
        {
            if (_templates == null)
            {
                _templates = new GameObject("Templates");

                _templates.SetActive(false);

                Object.DontDestroyOnLoad(_templates);

                _created.Add(_templates);
            }

            var template = new GameObject(name).AddComponent<T>();

            template.transform.SetParent(_templates.transform, false);

            return template;
        }

        protected T Create<T>(string name) where T : Component
        {
            var component = new GameObject(name).AddComponent<T>();

            _created.Add(component.gameObject);

            return component;
        }

        /// <summary>
        /// Creates a scene with these objects and begins it as the current level.
        /// </summary>
        protected Scene OpenLevel(params Component[] content)
        {
            _previousActive = SceneManager.GetActiveScene();

            _level = SceneManager.CreateScene("UEArchitecture Test Level " + Random.Range(0, int.MaxValue));

            foreach (var component in content)
            {
                SceneManager.MoveGameObjectToScene(component.gameObject, _level);
            }

            SceneManager.SetActiveScene(_level);

            World.Begin_Internal(_level);

            return _level;
        }

        protected static void Set(object target, string field, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var info = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (info != null)
                {
                    info.SetValue(target, value);

                    return;
                }
            }

            Assert.Fail($"{target.GetType().Name} has no field {field}");
        }

        protected static int IndexOf(string entry)
        {
            var index = TestGate.Log.IndexOf(entry);

            Assert.GreaterOrEqual(index, 0, $"\"{entry}\" was not logged. Log: {string.Join(", ", TestGate.Log)}");

            return index;
        }
    }
}
