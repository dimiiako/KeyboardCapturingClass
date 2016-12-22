using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Views.Animations;
using Android.Views.InputMethods;
using Android.Widget;
using Android.InputMethodServices;
using Android.Util;
using Java.Lang;
using Java.IO;
using System.IO;
using iPrognosis;
using Java.Util;
using Android.Preferences;
using Android.Media;
using Newtonsoft.Json;
using Android.Hardware;

namespace iPrognosis
{
    [Service(Label = "@string/simple_ime", Name = "iPrognosis.iPrognosisKeyboard", Permission = "android.permission.BIND_INPUT_METHOD")]
    [IntentFilter(new[] { "android.view.InputMethod" })]
    [MetaData("android.view.im", Resource = "@xml/method")]
    
    public class iPrognosisKeyboard : InputMethodService, KeyboardView.IOnKeyboardActionListener, KeyboardView.IOnTouchListener
    {
        static int NUM_OF_SUPPORTED_LANGS = 3; //Currently we support 3 languages(Eng, Gr, De)
        static int ENGLISH = 0;
        static int GREEK = 1;
        static int DEUTSCH = 2;

        OrientationEventListener myOrientationEventListener;
        private iPrognosisKeyboardView kv; //Revert to old by setting KeyboardView kv, change also @Layout: keyboard.xml iPrognosis.iPrognosisKeyboardView -> android.inputmethodservice.KeyboardView
        private Keyboard mykeyboard;
        private bool caps = false;
        public static bool rec;
        public bool flag = true;
        public int counter;
        PayloadKeyboard payloadKeyboard;
        public bool symbols = false;
        public bool symbols_shifted = false;
        public int langCode = ENGLISH;
        public int languageCode;
        bool[] activeLang = new bool[NUM_OF_SUPPORTED_LANGS]; // Helper table that stores the active languages of the keyboard. 
        //bool orientationChanged = false;

        public override View OnCreateInputView()
        {
            base.OnCreateInputView();
            
            Log.Debug("Inside OnCeate", "Yes");
            
            kv = (iPrognosisKeyboardView)LayoutInflater.Inflate(Resource.Layout.keyboard, null); //Revert to old by setting (KeyboardView)
            // Check if the keyboard runs for the first time or OnCreate is called due to orientation change
            if (mykeyboard == null) // This is the first time
            {
                languageCode = Resource.Xml.qwertyEN;

                for (int i = 0; i < NUM_OF_SUPPORTED_LANGS; i++)
                {
                    if (activeLang[i])
                    {
                        switch (i)
                        {
                            case 0:
                                mykeyboard = new Keyboard(this, Resource.Xml.qwertyEN);
                                langCode = ENGLISH;
                                break;
                            case 1:
                                mykeyboard = new Keyboard(this, Resource.Xml.qwertyGR);
                                langCode = GREEK;
                                break;
                            case 2:
                                mykeyboard = new Keyboard(this, Resource.Xml.qwertzDE);
                                langCode = DEUTSCH;
                                break;
                            default:
                                mykeyboard = new Keyboard(this, Resource.Xml.qwertyEN);
                                langCode = ENGLISH;
                                break;
                        }
                        break;
                    }
                }
            }
            else // Orientation changed
            {
                if (symbols_shifted)
                {
                    mykeyboard = new Keyboard(this, Resource.Xml.symbols_shifted);
                }
                else if (symbols)
                {
                    mykeyboard = new Keyboard(this, Resource.Xml.symbols);
                }
                else
                {
                    switch (langCode)
                    {
                        case 0:
                            mykeyboard = new Keyboard(this, Resource.Xml.qwertyEN);
                            break;
                        case 1:
                            mykeyboard = new Keyboard(this, Resource.Xml.qwertyGR);
                            break;
                        case 2:
                            mykeyboard = new Keyboard(this, Resource.Xml.qwertzDE);
                            break;
                        default:
                            mykeyboard = new Keyboard(this, Resource.Xml.qwertyEN);
                            break;
                    }
                }
            }
                
            
            kv.Keyboard = mykeyboard;
            kv.SetOnTouchListener(this);
            kv.OnKeyboardActionListener = this;

            return kv;
        }

        public override void OnFinishInputView(bool finishingInput)
        {
            Log.Debug("Inside OnFinishInput", "Yes");
            

            if (symbols || symbols_shifted)
            {
                changeSymbols();
                symbols = false;
                symbols_shifted = false;
            }

            //IList<Keyboard.Key> currKeys = kv.Keyboard.Keys;
            //for (int i = 0; i < currKeys.Count; i++)
            //{
            //    if (currKeys[i].Codes[1] == 32)
            //    {
            //        switch (currKeys[i].Label.ToString())
            //        {
            //            case "English":
            //                break;
            //            case "Ελληνικά":
            //                break;
            //            case "Deustch":
            //                break;

            //        }

            //        break;
            //    }

            //}
            
            //Log.Debug("Current keyboard", currKeys[0].Label.ToString());

            // Do stuff only if the user has typed something
            if (payloadKeyboard.DownTime.Count != 0) // Check if the payload is not empty
            {
                payloadKeyboard.StopDateTime = DateTime.Now;
                string payload = JsonConvert.SerializeObject(payloadKeyboard); // Create JSON string
                Intent intentToService = new Intent(this, typeof(KeyboardCapturingService)); // Create an intent in order to start the KeyboardCapturingService
                intentToService.PutExtra("KeyboardPayload", payload); // The filter of the intent is KeyboardPayload and is loaded with the JSON string
                StartService(intentToService); // Start the intent service
            }
            
            // Broadcast intent when keyboard disappears
            Intent KeyboardDownIntent = new Intent();
            KeyboardDownIntent.SetAction("KEYBOARD_DOWN");
            SendBroadcast(KeyboardDownIntent);

            // Clear before closing the keyboard
            payloadKeyboard = null;
            base.OnFinishInputView(finishingInput);
        }

        public override bool OnShowInputRequested(ShowFlags flags, bool configChange)
        {
            Log.Debug("Inside OnShowInput", "Yes");
            base.OnShowInputRequested(flags, configChange);

            // Get an object of the current status of the Preferences menu for handling language issues
            ISharedPreferences sharedPref = PreferenceManager.GetDefaultSharedPreferences(this);

            // Check if no language has been selected from the preferences menu and set the default languages (English + System language)
            // This situation appears the first time that the iPrognosis keyboard is selected as the default keyboard
            // or if the user deliberately unchecks every language on the preferences menu
            if (!sharedPref.GetBoolean("en_set", false) && !sharedPref.GetBoolean("el_set", false) && !sharedPref.GetBoolean("de_set", false))
            {
                // We consider English as the first default language
                ISharedPreferencesEditor editor = sharedPref.Edit();
                editor.PutBoolean("en_set", true);
                editor.Commit();

                // The system language is set as the second default language
                switch (Locale.Default.Language.ToString())
                {
                    case "el": // Greek
                        editor.PutBoolean("el_set", true);
                        editor.Commit();
                        break;

                    case "de": // German
                        editor.PutBoolean("de_set", true);
                        editor.Commit();
                        break;
                }
            }

            // Store the active languages, based on the preferences menu, to this helper table
            activeLang[0] = sharedPref.GetBoolean("en_set", false);
            activeLang[1] = sharedPref.GetBoolean("el_set", false);
            activeLang[2] = sharedPref.GetBoolean("de_set", false);

            // Initialise the payloadKeyboard if it has previously been destroyed (OnFinishInputView has been called)
            if (payloadKeyboard == null)
            {
                payloadKeyboard = new PayloadKeyboard();
                payloadKeyboard.IsSoundOn = sharedPref.GetBoolean("sound_on", false);
                payloadKeyboard.IsVibrationOn = sharedPref.GetBoolean("vibrate_on", false);
                payloadKeyboard.StartDateTime = DateTime.Now;
            }

            // Broadcast intent when keyboard is to appear
            Intent KeyboardUpIntent = new Intent();
            KeyboardUpIntent.SetAction("KEYBOARD_UP");
            SendBroadcast(KeyboardUpIntent);

            return true;
        }

        public void OnKey([GeneratedEnum] Android.Views.Keycode primaryCode, [GeneratedEnum] Android.Views.Keycode[] keyCodes)
        {
            AudioManager am = (AudioManager)GetSystemService(AudioService);
            Vibrator mVibrator = (Vibrator)GetSystemService(VibratorService);
            IInputConnection ic = CurrentInputConnection;
            ISharedPreferences currSharedPref = PreferenceManager.GetDefaultSharedPreferences(this);

            switch ((int)primaryCode)
            {

                case -5: //Delete
                    ic.SendKeyEvent(new KeyEvent(KeyEventActions.Down, Android.Views.Keycode.Del));
                    payloadKeyboard.NumDels += 1;
                    //ic.DeleteSurroundingText(1, 0);

                    break;
                case -1: //Shift
                    caps = !caps;
                    mykeyboard.SetShifted(caps);
                    kv.InvalidateAllKeys();
                    Log.Debug("", mykeyboard.IsShifted.ToString());
                    break;
                case -4: //Done
                    SendDefaultEditorAction(true); //Fixes enter key action in search boxes
                    ic.SendKeyEvent(new KeyEvent(KeyEventActions.Down, Android.Views.Keycode.Enter));
                    break;
                case -101:
                    //KEYCODE_LANGUAGE_SWITCH:
                    handleLanguageSwitch();
                    break;
                case -35:
                    changeSymbols();
                    break;
                case -2:
                    changeSymbols();
                    break;
                case -200:
                    changeSymbolsShifted();
                    break;
                case -201:
                    changeSymbolsShifted();
                    break;

                default:
                    
                    // If the Sounds option in the Preferences menu is selected, play sound
                    if (currSharedPref.GetBoolean("sound_on", false))
                    {
                        am.PlaySoundEffect(SoundEffect.Standard);
                    }

                    // If the Vibration option in the Preferences menu is selected, vibrate
                    if (currSharedPref.GetBoolean("vibrate_on", false))
                    {
                        mVibrator.Vibrate(20);
                    }

                    char code = (char)primaryCode;
                    if (Character.IsLetter(code) && caps)
                    {
                        code = Character.ToUpperCase(code);
                    }
                    ic.CommitText(Java.Lang.String.ValueOf(code), 1);
                    break;
            }
        }

        //Using this function when the icon of Languge change is pressed, a basic implementation with a boolean flag
        private View changeSymbols()
        {
            symbols_shifted = false;
            caps = false;
            if (symbols == false)
            {

                mykeyboard = new Keyboard(this, Resource.Xml.symbols);
                symbols = true;
                kv.Keyboard = mykeyboard;
                kv.SetOnTouchListener(this);
                kv.OnKeyboardActionListener = this;
                return kv;
            }
            else
            {
                symbols = false;
                switch (langCode)
                {
                    case 0: // English
                        mykeyboard = new Keyboard(this, Resource.Xml.qwertyEN);
                        kv.Keyboard = mykeyboard;
                        kv.SetOnTouchListener(this);
                        kv.OnKeyboardActionListener = this;
                        Log.Debug("", mykeyboard.IsShifted.ToString());
                        break;
                    case 1: // Greek
                        mykeyboard = new Keyboard(this, Resource.Xml.qwertyGR);
                        kv.Keyboard = mykeyboard;
                        kv.SetOnTouchListener(this);
                        kv.OnKeyboardActionListener = this;
                        Log.Debug("", mykeyboard.IsShifted.ToString());
                        break;
                    case 2: // Deutsch
                        mykeyboard = new Keyboard(this, Resource.Xml.qwertzDE);
                        kv.Keyboard = mykeyboard;
                        kv.SetOnTouchListener(this);
                        kv.OnKeyboardActionListener = this;
                        Log.Debug("", mykeyboard.IsShifted.ToString());
                        break;
                }

                return kv;
            }
        }

        private View changeSymbolsShifted()
        {
            caps = false;
            if (!symbols_shifted)
            {
                mykeyboard = new Keyboard(this, Resource.Xml.symbols_shifted);
                symbols_shifted = true;
            }
            else
            {
                mykeyboard = new Keyboard(this, Resource.Xml.symbols);
                symbols_shifted = false;
            }

            kv.Keyboard = mykeyboard;
            kv.SetOnTouchListener(this);
            kv.OnKeyboardActionListener = this;
            return kv;
        }

        private View handleLanguageSwitch()
        {
            Log.Debug("LangCode", langCode.ToString());
            Log.Debug("Active Lang Count", activeLang.Length.ToString());
            caps = false;
            langCode += 1;

            if (langCode == activeLang.Length)
            {
                langCode = 0;
            }

            while (!activeLang[langCode])
            {
                langCode += 1;
                if (langCode == activeLang.Length)
                {
                    langCode = 0;
                }
            }

            switch (langCode)
            {
                case 0:
                    mykeyboard = new Keyboard(this, Resource.Xml.qwertyEN);
                    kv.Keyboard = mykeyboard;
                    kv.SetOnTouchListener(this);
                    kv.OnKeyboardActionListener = this;
                    break;
                case 1:
                    mykeyboard = new Keyboard(this, Resource.Xml.qwertyGR);
                    kv.Keyboard = mykeyboard;
                    kv.SetOnTouchListener(this);
                    kv.OnKeyboardActionListener = this;
                    break;
                case 2:
                    mykeyboard = new Keyboard(this, Resource.Xml.qwertzDE);
                    kv.Keyboard = mykeyboard;
                    kv.SetOnTouchListener(this);
                    kv.OnKeyboardActionListener = this;
                    break;
            }

            return kv;

        }

        private IBinder getToken()
        {
            Dialog dialog = Window;
            if (dialog == null)
            {
                return null;
            }
            Window window = dialog.Window;
            if (window == null)
            {
                return null;
            }

            return Window.Window.Attributes.Token;
        }
        
        public bool OnTouch(View v, MotionEvent me)
        {
            if (me.Action == MotionEventActions.Down)
            {
                payloadKeyboard.DownTime.Add(me.EventTime); // When a keyboard button is pressed down, the EventTime is added to the data object (k)
            }
            else if (me.Action == MotionEventActions.Up)
            {
                try
                {
                    payloadKeyboard.UpTime.Add(me.EventTime);
                    payloadKeyboard.PressureValue.Add(me.GetPressure(me.GetPointerId(0)));
                    payloadKeyboard.IsLongPress.Add(kv.getFlagLongPress());
                    kv.setFlagLongPress();
                    rec = true;
                }
                catch
                {
                    Log.Debug("{0} Exception caught.", "Yes");
                }
            }
            return false;
        }

        public void OnPress([GeneratedEnum] Android.Views.Keycode primaryCode)
        {
            if ((int)primaryCode == -1 || (int)primaryCode == -5 || (int)primaryCode == -4 || (int)primaryCode == -101 || (int)primaryCode == -35 || (int)primaryCode == -2 || (int)primaryCode == 32 || (int)primaryCode == -200 || (int)primaryCode == -201)
            {
                kv.PreviewEnabled = false;
            }
            else
            {
                kv.PreviewEnabled = true;
            }
        }

        public void OnRelease([GeneratedEnum] Android.Views.Keycode primaryCode)
        {
            if ((int)primaryCode == -1 || (int)primaryCode == -4 || (int)primaryCode == -101 || (int)primaryCode == -35 || (int)primaryCode == -2 || (int)primaryCode == 32 || (int)primaryCode == -200 || (int)primaryCode == -201)
            {
                kv.PreviewEnabled = true;
            }
        }

        public void OnText(ICharSequence text)
        {
        }

        public void SwipeDown()
        {
        }

        public void SwipeLeft()
        {
        }

        public void SwipeRight()
        {
        }

        public void SwipeUp()
        {
        }
    }

    //public class KeyboardOrientationListener : OrientationEventListener
    //{
    //    iPrognosisKeyboard _parentKeyboard;

    //    public KeyboardOrientationListener(iPrognosisKeyboard _parentKeyboard):base()
    //    {

    //    }
    //}
}