#!/usr/bin/env python3
# -*- coding: utf-8 -*-
# filename: config.py
# modified: 2019-09-10

import os
import re
from collections import OrderedDict
from configparser import DuplicateSectionError, RawConfigParser

from .const import DEFAULT_CONFIG_INI
from .course import Course
from .environ import Environ
from .exceptions import UserInputException
from .rule import Delay, Mutex
from .secrets import IAAA_PASSWORD_ENV, get_password
from .utils import Singleton

_reNamespacedSection = re.compile(r'^\s*(?P<ns>[^:]+?)\s*:\s*(?P<id>[^,]+?)\s*$')
_reCommaSep = re.compile(r'\s*,\s*')

environ = Environ()


class BaseConfig(object):

    def __init__(self, config_file=None):
        if self.__class__ is __class__:
            raise NotImplementedError
        file = os.path.normpath(os.path.abspath(config_file))
        if not os.path.exists(file):
            raise FileNotFoundError("Config file was not found: %s" % file)
        self._config = RawConfigParser()
        self._config.read(file, encoding="utf-8-sig")

    def get(self, section, key, *args, **kwargs):
        return self._config.get(section, key, *args, **kwargs)

    def getint(self, section, key, *args, **kwargs):
        return self._config.getint(section, key, *args, **kwargs)

    def getfloat(self, section, key, *args, **kwargs):
        return self._config.getfloat(section, key, *args, **kwargs)

    def getboolean(self, section, key, *args, **kwargs):
        return self._config.getboolean(section, key, *args, **kwargs)

    def getdict(self, section, options):
        if not isinstance(options, (list, tuple, set)):
            raise TypeError("options must be a list, tuple or set")
        items = dict(self._config.items(section))
        if not all( k in items for k in options ):
            raise UserInputException("Incomplete course in section %r, %s must all exist." % (section, options))
        d = { k: items[k] for k in options }
        return d

    def getlist(self, section, option, *args, **kwargs):
        v = self.get(section, option, *args, **kwargs)
        return _reCommaSep.split(v)

    def ns_sections(self, ns):
        ns = ns.strip()
        ns_sects = OrderedDict() # { id: str(section) }
        for s in self._config.sections():
            mat = _reNamespacedSection.match(s)
            if mat is None:
                continue
            if mat.group('ns') != ns:
                continue
            id_ = mat.group('id')
            if id_ in ns_sects:
                raise DuplicateSectionError("%s:%s" % (ns, id_))
            ns_sects[id_] = s
        return [ (id_, s) for id_, s in ns_sects.items() ] # [ (id, str(section)) ]


class AutoElectiveConfig(BaseConfig, metaclass=Singleton):

    def __init__(self):
        super().__init__(environ.config_ini or DEFAULT_CONFIG_INI)

    ## Constraints

    ALLOWED_IDENTIFY = ("bzx","bfx")

    ## Model

    # [user]

    @property
    def iaaa_id(self):
        return self.get("user", "student_id")

    @property
    def iaaa_password(self):
        return get_password("iaaa", IAAA_PASSWORD_ENV)

    @property
    def is_dual_degree(self):
        return self.getboolean("user", "dual_degree")

    @property
    def identity(self):
        return self.get("user", "identity").lower()

    # [client]

    @property
    def refresh_interval(self):
        return self.getfloat("client", "refresh_interval")

    @property
    def refresh_random_deviation(self):
        return self.getfloat("client", "random_deviation")

    @property
    def iaaa_client_timeout(self):
        return self.getfloat("client", "iaaa_client_timeout")

    @property
    def elective_client_timeout(self):
        return self.getfloat("client", "elective_client_timeout")

    @property
    def elective_client_pool_size(self):
        return self.getint("client", "elective_client_pool_size")

    @property
    def elective_client_max_life(self):
        return self.getint("client", "elective_client_max_life")

    @property
    def login_loop_interval(self):
        return self.getfloat("client", "login_loop_interval")

    @property
    def is_print_mutex_rules(self):
        return self.getboolean("client", "print_mutex_rules")

    @property
    def is_debug_print_request(self):
        return self.getboolean("client", "debug_print_request")

    @property
    def is_debug_dump_request(self):
        return self.getboolean("client", "debug_dump_request")

    # [course]

    @property
    def courses(self):
        cs = OrderedDict()  # { id: Course }
        rcs = {}
        for id_, s in self.ns_sections('course'):
            d = self.getdict(s, ('name','class','school'))
            d.update(class_no=d.pop('class'))
            c = Course(**d)
            cs[id_] = c
            rid = rcs.get(c)
            if rid is not None:
                raise UserInputException("Duplicated courses in sections 'course:%s' and 'course:%s'" % (rid, id_))
            rcs[c] = id_
        return cs

    # [swap] - optional drop fields in course sections

    @property
    def swaps(self):
        """Returns { course_id: Course_to_drop } for courses that need swap."""
        ss = {}
        for id_, s in self.ns_sections('course'):
            present = [self._config.has_option(s, key) for key in
                       ('drop_name', 'drop_class', 'drop_school')]
            if any(present) and not all(present):
                raise UserInputException(
                    "Incomplete swap in section %r; drop_name, drop_class and drop_school must all exist" % s
                )
            if all(present):
                drop_name = self.get(s, 'drop_name')
                drop_class = self.get(s, 'drop_class')
                drop_school = self.get(s, 'drop_school')
                ss[id_] = Course(drop_name, drop_class, drop_school)
        return ss

    @property
    def enable_unsafe_auto_swap(self):
        return self.getboolean("safety", "enable_unsafe_auto_swap", fallback=False)

    @property
    def captcha_provider(self):
        return self.get("captcha", "provider", fallback="local").strip().lower()

    # [mutex]

    @property
    def mutexes(self):
        ms = OrderedDict()  # { id: Mutex }
        for id_, s in self.ns_sections('mutex'):
            lst = self.getlist(s, 'courses')
            ms[id_] = Mutex(lst)
        return ms

    # [delay]

    @property
    def delays(self):
        ds = OrderedDict()  # { id: Delay }
        cid_id = {} # { cid: id }
        for id_, s in self.ns_sections('delay'):
            cid = self.get(s, 'course')
            threshold = self.getint(s, 'threshold')
            if not threshold > 0:
                raise UserInputException("Invalid threshold %d in 'delay:%s', threshold > 0 must be satisfied" % (threshold, id_))
            id0 = cid_id.get(cid)
            if id0 is not None:
                raise UserInputException("Duplicated delays of 'course:%s' in 'delay:%s' and 'delay:%s'" % (cid, id0, id_))
            cid_id[cid] = id_
            ds[id_] = Delay(cid, threshold)
        return ds

    ## Method

    def check_identify(self, identity):
        limited = self.__class__.ALLOWED_IDENTIFY
        if identity not in limited:
            raise ValueError("unsupported identity %s for elective, identity must be in %s" % (identity, limited))

    def get_user_subpath(self):
        if not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", self.iaaa_id):
            raise UserInputException("student_id may only contain letters, digits, '_' and '-'")
        if self.is_dual_degree:
            identity = self.identity
            self.check_identify(identity)
            if identity == "bfx":
                return "%s_%s" % (self.iaaa_id, identity)
        return self.iaaa_id

    def validate(self):
        self.check_identify(self.identity)
        if self.refresh_interval < 4:
            raise UserInputException("refresh_interval must be at least 4 seconds")
        if not 0 <= self.refresh_random_deviation <= 0.5:
            raise UserInputException("random_deviation must be between 0 and 0.5")
        if not 1 <= self.elective_client_pool_size <= 4:
            raise UserInputException("elective_client_pool_size must be between 1 and 4")
        if self.iaaa_client_timeout <= 0 or self.elective_client_timeout <= 0:
            raise UserInputException("client timeouts must be positive")
        if self.login_loop_interval < 1:
            raise UserInputException("login_loop_interval must be at least 1 second")
        if self.elective_client_max_life != -1 and self.elective_client_max_life < 60:
            raise UserInputException("elective_client_max_life must be -1 or at least 60 seconds")
        if self.captcha_provider != "local":
            raise UserInputException("captcha.provider must remain 'local'")
        courses = self.courses
        if not courses:
            raise UserInputException("At least one course must be configured")
        if len(courses) > 50:
            raise UserInputException("At most 50 courses may be configured")
        for course in courses.values():
            if course.class_no < 1:
                raise UserInputException("Course class numbers must be positive")
            if len(course.name) > 200 or len(course.school) > 200:
                raise UserInputException("Course names and schools must be at most 200 characters")
        swaps = self.swaps
        if swaps and not self.enable_unsafe_auto_swap:
            raise UserInputException(
                "自动换课存在退课后无法选回的风险；如确认承担风险，请设置 "
                "[safety] enable_unsafe_auto_swap = true"
            )
        for cid, drop_course in swaps.items():
            if courses[cid] == drop_course:
                raise UserInputException("course:%s cannot swap with itself" % cid)
        self.get_user_subpath()
